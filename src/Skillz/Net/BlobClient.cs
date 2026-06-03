using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Skillz.Net;

internal sealed class BlobClient(IHttpClientFactory httpClientFactory, IGitHubTokenProvider tokenProvider) : IBlobClient
{
    internal const string HttpClientName = "Skillz.GitHub";

    /// <summary>
    /// Hard cap on a single response body. Skill manifests and tree listings are small; this
    /// bounds memory against a hostile or runaway endpoint. Applied as
    /// <c>MaxResponseContentBufferSize</c> on the named client (buffered reads) and enforced
    /// directly for streamed reads where that limit does not apply.
    /// </summary>
    internal const long MaxResponseBytes = 8 * 1024 * 1024;

    private static readonly TimeSpan s_fetchTimeout = TimeSpan.FromSeconds(10);

    private readonly object _rateLimitLock = new();
    private bool _rateLimitedThisSession;

    public async Task<RepoTree?> FetchTreeAsync(
        string owner,
        string repo,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var ownerRepo = $"{owner}/{repo}";
        var branches = string.IsNullOrEmpty(@ref) ? ["HEAD", "main", "master"] : new[] { @ref };

        var rateLimitedAtStart = IsRateLimited();

        if (rateLimitedAtStart)
        {
            var token = await tokenProvider.FindTokenAsync(cancellationToken);
            if (token is null)
            {
                return null;
            }

            foreach (var branch in branches)
            {
                var result = await FetchTreeBranchAsync(ownerRepo, branch, token, cancellationToken);
                if (result.Tree is not null)
                {
                    return result.Tree;
                }
            }

            return null;
        }

        var rateLimited = false;
        foreach (var branch in branches)
        {
            var result = await FetchTreeBranchAsync(ownerRepo, branch, token: null, cancellationToken);
            if (result.Tree is not null)
            {
                return result.Tree;
            }

            if (result.RateLimited)
            {
                rateLimited = true;
                break;
            }
        }

        if (!rateLimited)
        {
            return null;
        }

        MarkRateLimited();
        var fallbackToken = await tokenProvider.FindTokenAsync(cancellationToken);
        if (fallbackToken is null)
        {
            return null;
        }

        foreach (var branch in branches)
        {
            var result = await FetchTreeBranchAsync(ownerRepo, branch, fallbackToken, cancellationToken);
            if (result.Tree is not null)
            {
                return result.Tree;
            }
        }

        return null;
    }

    private async Task<BranchFetchResult> FetchTreeBranchAsync(
        string ownerRepo,
        string branch,
        string? token,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{ownerRepo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        request.Headers.UserAgent.ParseAdd("skillz-cli");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(s_fetchTimeout);

            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength is { } declared && declared > MaxResponseBytes)
                {
                    return new BranchFetchResult(null, RateLimited: false);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                await using var bounded = new MaxBytesStream(stream, MaxResponseBytes);
                var data = await JsonSerializer.DeserializeAsync(
                    bounded,
                    JsonSourceGenerationContext.Default.GitHubTreeResponse,
                    timeoutCts.Token);

                if (data is null)
                {
                    return new BranchFetchResult(null, RateLimited: false);
                }

                var tree = new RepoTree(data.Sha, branch, [.. data.Tree ?? []]);
                return new BranchFetchResult(tree, RateLimited: false);
            }

            var rateLimited = response.StatusCode == HttpStatusCode.Forbidden && GetRemaining(response) == "0";
            return new BranchFetchResult(null, rateLimited);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BlobFetchTimeoutException(url);
        }
        catch (HttpRequestException)
        {
            return new BranchFetchResult(null, RateLimited: false);
        }
        catch (JsonException)
        {
            return new BranchFetchResult(null, RateLimited: false);
        }
    }

    private static string? GetRemaining(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var values))
        {
            foreach (var value in values)
            {
                return value;
            }
        }

        return null;
    }

    private bool IsRateLimited()
    {
        lock (_rateLimitLock)
        {
            return _rateLimitedThisSession;
        }
    }

    private void MarkRateLimited()
    {
        lock (_rateLimitLock)
        {
            _rateLimitedThisSession = true;
        }
    }

    private readonly record struct BranchFetchResult(RepoTree? Tree, bool RateLimited);
}
