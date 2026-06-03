using Skillz.Net;

namespace Skillz.Tests.TestServices;

internal sealed class TestBlobClient : IBlobClient
{
    public Func<string, string, string?, RepoTree?>? OnFetchTree { get; set; }

    public Func<string, string, string, string?, string?>? OnFetchFile { get; set; }

    public Task<RepoTree?> FetchTreeAsync(
        string owner,
        string repo,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var result = OnFetchTree?.Invoke(owner, repo, @ref);
        return Task.FromResult(result);
    }

    public Task<string?> FetchFileAsync(
        string owner,
        string repo,
        string path,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var result = OnFetchFile?.Invoke(owner, repo, path, @ref);
        return Task.FromResult(result);
    }
}
