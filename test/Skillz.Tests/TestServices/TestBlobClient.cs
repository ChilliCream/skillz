using Skillz.Net;

namespace Skillz.Tests.TestServices;

internal sealed class TestBlobClient : IBlobClient
{
    public Func<string, string, string?, RepoTree?>? OnFetchTree { get; set; }

    public Task<RepoTree?> FetchTreeAsync(
        string owner,
        string repo,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var result = OnFetchTree?.Invoke(owner, repo, @ref);
        return Task.FromResult(result);
    }
}
