namespace Skillz.Net;

internal interface IBlobClient
{
    Task<RepoTree?> FetchTreeAsync(
        string owner,
        string repo,
        string? @ref,
        string? path,
        CancellationToken cancellationToken);

    Task<string?> FetchFileAsync(
        string owner,
        string repo,
        string path,
        string? @ref,
        CancellationToken cancellationToken);
}
