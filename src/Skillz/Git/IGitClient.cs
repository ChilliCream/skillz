namespace Skillz.Git;

internal interface IGitClient
{
    Task<string> CloneAsync(string url, string targetDir, string? @ref, CancellationToken cancellationToken);

    Task<string?> GetDefaultBranchAsync(string url, CancellationToken cancellationToken);
}
