namespace Skillz.Net;

/// <summary>
/// Supplies a GitHub access token for authenticated API calls, discovered from the
/// environment or the <c>gh</c> CLI. Authentication is optional, so callers fall back
/// to unauthenticated requests when no token is available.
/// </summary>
internal interface IGitHubTokenProvider
{
    /// <summary>
    /// Returns a usable token, or <see langword="null"/> when none is configured.
    /// </summary>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}
