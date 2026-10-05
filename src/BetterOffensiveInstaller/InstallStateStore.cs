using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BetterOffensiveInstaller;

internal sealed record InstallState(
    string? ReleaseTag,
    DateTimeOffset InstalledAtUtc,
    IReadOnlyList<string> InstalledFiles,
    IReadOnlyList<string> InstalledDirectories,
    IReadOnlyList<ManagedExclusion> Exclusions,
    IReadOnlyList<BackedUpFile>? BackedUpFiles = null);

internal sealed record BackedUpFile(string OriginalRelativePath, string BackupRelativePath);

internal static class InstallStateStore
{
    private const string StateFileName = ".grmod-installer-state.json";
    private const string RegistryRootPath = @"Software\BetterOffensiveInstaller\InstallState";
    private const string StateValueName = "StateJson";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static bool Exists(string installPath)
    {
        return RegistryContainsState(installPath) ||
               File.Exists(GetLegacyStateFilePath(installPath));
    }

    public static async Task<InstallState?> TryLoadAsync(string installPath, CancellationToken cancellationToken = default)
    {
        if (TryLoadFromRegistry(installPath, out var registryState))
        {
            return registryState;
        }

        var legacyStateFilePath = GetLegacyStateFilePath(installPath);

        if (!File.Exists(legacyStateFilePath))
        {
            return null;
        }

        await using var stream = new FileStream(legacyStateFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var state = await JsonSerializer.DeserializeAsync<InstallState>(stream, SerializerOptions, cancellationToken);

        return state ?? throw new InstallerAppException(InstallerErrorKind.InvalidInstallState);
    }

    public static async Task SaveAsync(string installPath, InstallState state, CancellationToken cancellationToken = default)
    {
        var stateJson = JsonSerializer.Serialize(state, SerializerOptions);

        await Task.Run(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(GetRegistryKeyPath(installPath), writable: true)
                ?? throw new InstallerAppException(InstallerErrorKind.InvalidInstallState);

            key.SetValue(StateValueName, stateJson, RegistryValueKind.String);
        }, cancellationToken);

        DeleteLegacyStateFile(installPath);
    }

    public static void Delete(string installPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GetRegistryKeyPath(installPath), writable: true);
            key?.DeleteValue(StateValueName, throwOnMissingValue: false);
        }
        catch
        {
        }

        try
        {
            using var rootKey = Registry.CurrentUser.OpenSubKey(RegistryRootPath, writable: true);
            rootKey?.DeleteSubKeyTree(GetInstallPathHash(installPath), throwOnMissingSubKey: false);
        }
        catch
        {
        }

        DeleteLegacyStateFile(installPath);
    }

    private static string GetLegacyStateFilePath(string installPath)
    {
        return Path.Combine(Path.GetFullPath(installPath), StateFileName);
    }

    private static string GetRegistryKeyPath(string installPath)
    {
        return Path.Combine(RegistryRootPath, GetInstallPathHash(installPath));
    }

    private static bool RegistryContainsState(string installPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GetRegistryKeyPath(installPath), writable: false);
            return key?.GetValue(StateValueName) is string { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadFromRegistry(string installPath, out InstallState? state)
    {
        state = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GetRegistryKeyPath(installPath), writable: false);
            var stateJson = key?.GetValue(StateValueName) as string;

            if (string.IsNullOrWhiteSpace(stateJson))
            {
                return false;
            }

            state = JsonSerializer.Deserialize<InstallState>(stateJson, SerializerOptions)
                ?? throw new InstallerAppException(InstallerErrorKind.InvalidInstallState);

            return true;
        }
        catch (InstallerAppException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string GetInstallPathHash(string installPath)
    {
        var normalizedInstallPath = Path.GetFullPath(installPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedInstallPath));
        return Convert.ToHexString(hashBytes);
    }

    private static void DeleteLegacyStateFile(string installPath)
    {
        try
        {
            var legacyStateFilePath = GetLegacyStateFilePath(installPath);
            if (File.Exists(legacyStateFilePath))
            {
                File.Delete(legacyStateFilePath);
            }
        }
        catch
        {
        }
    }
}
