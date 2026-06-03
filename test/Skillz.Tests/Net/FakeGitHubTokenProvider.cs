using Skillz.Net;

namespace Skillz.Tests.Net;

internal sealed class FakeGitHubTokenProvider : IGitHubTokenProvider
{
    private readonly Func<string?> _tokenFactory;

    public FakeGitHubTokenProvider(Func<string?> tokenFactory)
    {
        _tokenFactory = tokenFactory;
    }

    public int CallCount { get; private set; }

    public Task<string?> FindTokenAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_tokenFactory());
    }
}
