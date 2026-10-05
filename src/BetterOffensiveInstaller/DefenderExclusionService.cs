using System.Diagnostics;
using System.Text;

namespace BetterOffensiveInstaller;

internal sealed record ManagedExclusion(string Path, bool AddedByInstaller);

internal sealed class DefenderExclusionService
{
    public async Task<bool> IsDefenderAvailableAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunPowerShellScriptAsync(BuildAvailabilityScript(), cancellationToken);
        return bool.TryParse(output.Trim(), out var isAvailable) && isAvailable;
    }

    public async Task<IReadOnlyList<ManagedExclusion>> EnsureExcludedAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var normalizedPaths = NormalizeUniquePaths(paths);

        if (normalizedPaths.Count == 0)
        {
            return [];
        }

        var existingPaths = new HashSet<string>(await GetExcludedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        var pathsToAdd = normalizedPaths.Where(path => !existingPaths.Contains(path)).ToArray();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (pathsToAdd.Length > 0)
        {
            var output = await RunPowerShellScriptAsync(BuildAddScript(pathsToAdd), cancellationToken);

            foreach (var path in ParseOutputLines(output))
            {
                addedPaths.Add(path);
            }
        }

        return normalizedPaths
            .Select(path => new ManagedExclusion(path, addedPaths.Contains(path)))
            .ToArray();
    }

    public async Task RemoveExclusionsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var normalizedPaths = NormalizeUniquePaths(paths);

        if (normalizedPaths.Count == 0)
        {
            return;
        }

        if (!await IsDefenderAvailableAsync(cancellationToken))
        {
            return;
        }

        await RunPowerShellScriptAsync(BuildRemoveScript(normalizedPaths), cancellationToken);
    }

    private static string BuildAvailabilityScript()
    {
        return """
$hasGet = $null -ne (Get-Command Get-MpPreference -ErrorAction SilentlyContinue)
$hasAdd = $null -ne (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)
$hasRemove = $null -ne (Get-Command Remove-MpPreference -ErrorAction SilentlyContinue)
Write-Output ($hasGet -and $hasAdd -and $hasRemove)
""";
    }

    private static async Task<IReadOnlyList<string>> GetExcludedPathsAsync(CancellationToken cancellationToken)
    {
        var output = await RunPowerShellScriptAsync(BuildListScript(), cancellationToken);
        return ParseOutputLines(output);
    }

    private static async Task<string> RunPowerShellScriptAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPowerShellPath(),
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("PowerShell could not be started.");
        }

        await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError)
                ? "Microsoft Defender exclusions could not be updated."
                : standardError.Trim());
        }

        return standardOutput;
    }

    private static string BuildListScript()
    {
        return """
$ErrorActionPreference = 'Stop'
if ($null -eq (Get-Command Get-MpPreference -ErrorAction SilentlyContinue)) {
    throw 'Microsoft Defender PowerShell cmdlets are unavailable on this system.'
}

foreach ($item in @((Get-MpPreference).ExclusionPath)) {
    if (-not [string]::IsNullOrWhiteSpace($item)) {
        [System.IO.Path]::GetFullPath($item)
    }
}
""";
    }

    private static string BuildAddScript(IReadOnlyList<string> paths)
    {
        return $$"""
$ErrorActionPreference = 'Stop'
if ($null -eq (Get-Command Get-MpPreference -ErrorAction SilentlyContinue) -or
    $null -eq (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) {
    throw 'Microsoft Defender PowerShell cmdlets are unavailable on this system.'
}

$currentPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($item in @((Get-MpPreference).ExclusionPath)) {
    if (-not [string]::IsNullOrWhiteSpace($item)) {
        [void]$currentPaths.Add([System.IO.Path]::GetFullPath($item))
    }
}

foreach ($path in {{BuildPowerShellArray(paths)}}) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $currentPaths.Contains($fullPath)) {
        Add-MpPreference -ExclusionPath $fullPath | Out-Null
        [void]$currentPaths.Add($fullPath)
        Write-Output $fullPath
    }
}
""";
    }

    private static string BuildRemoveScript(IReadOnlyList<string> paths)
    {
        return $$"""
$ErrorActionPreference = 'Stop'
if ($null -eq (Get-Command Get-MpPreference -ErrorAction SilentlyContinue) -or
    $null -eq (Get-Command Remove-MpPreference -ErrorAction SilentlyContinue)) {
    throw 'Microsoft Defender PowerShell cmdlets are unavailable on this system.'
}

$currentPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($item in @((Get-MpPreference).ExclusionPath)) {
    if (-not [string]::IsNullOrWhiteSpace($item)) {
        [void]$currentPaths.Add([System.IO.Path]::GetFullPath($item))
    }
}

foreach ($path in {{BuildPowerShellArray(paths)}}) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if ($currentPaths.Contains($fullPath)) {
        Remove-MpPreference -ExclusionPath $fullPath | Out-Null
    }
}
""";
    }

    private static string BuildPowerShellArray(IReadOnlyList<string> paths)
    {
        var lines = paths
            .Select(path => $"    {ToPowerShellStringLiteral(path)}");

        return "@(\r\n" + string.Join(",\r\n", lines) + "\r\n)";
    }

    private static string ToPowerShellStringLiteral(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static List<string> NormalizeUniquePaths(IEnumerable<string> paths)
    {
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            uniquePaths.Add(Path.GetFullPath(path.Trim()));
        }

        return uniquePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ParseOutputLines(string output)
    {
        return output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetPowerShellPath()
    {
        return Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    }
}
