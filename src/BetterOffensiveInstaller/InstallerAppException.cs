namespace BetterOffensiveInstaller;

public enum InstallerErrorKind
{
    InvalidArchivePath,
    GitHubEmptyPayload,
    GitHubMissingAssets,
    InvalidInstallState,
    InvalidInstallStatePath
}

public sealed class InstallerAppException : Exception
{
    public InstallerAppException(InstallerErrorKind kind)
        : base(kind.ToString())
    {
        Kind = kind;
    }

    public InstallerErrorKind Kind { get; }
}
