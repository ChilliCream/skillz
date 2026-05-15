namespace Skillz.Net;

internal interface IGitHubTokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}
