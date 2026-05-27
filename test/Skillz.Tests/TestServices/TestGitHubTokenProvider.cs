using Skillz.Net;

namespace Skillz.Tests.TestServices;

internal sealed class TestGitHubTokenProvider : IGitHubTokenProvider
{
    public Func<string?>? OnGetToken { get; set; }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var result = OnGetToken?.Invoke();
        return Task.FromResult(result);
    }
}
