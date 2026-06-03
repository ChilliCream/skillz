namespace Skillz.Install;

internal enum InstallMode
{
    Symlink,
    Copy
}

internal sealed record InstallResult(
    bool Success,
    string Path,
    string? CanonicalPath = null,
    InstallMode Mode = InstallMode.Symlink,
    bool SymlinkFailed = false,
    bool Skipped = false,
    string? Error = null);

internal sealed record InstallOptions(bool Global = false, string? WorkingDirectory = null, InstallMode Mode = InstallMode.Symlink);
