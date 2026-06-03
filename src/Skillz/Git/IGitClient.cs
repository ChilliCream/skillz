namespace Skillz.Git;

/// <summary>
/// Wraps the <c>git</c> command-line so skill sources backed by a repository can be
/// cloned and inspected without the rest of the app shelling out directly.
/// </summary>
internal interface IGitClient
{
    /// <summary>
    /// Clones <paramref name="url"/> (optionally at <paramref name="ref"/>) into
    /// <paramref name="targetDirectory"/> and returns the directory it cloned into.
    /// </summary>
    Task<string> CloneAsync(string url, string targetDirectory, string? @ref, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the remote's default branch, or <see langword="null"/> when it cannot be determined.
    /// </summary>
    Task<string?> FindDefaultBranchAsync(string url, CancellationToken cancellationToken);
}
