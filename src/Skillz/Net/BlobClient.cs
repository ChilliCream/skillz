using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Skillz.Net;

internal sealed class BlobClient : IBlobClient
{
    internal const string HttpClientName = "Skillz.GitHub";

    private static readonly TimeSpan s_fetchTimeout = TimeSpan.FromSeconds(10);

    private static readonly object s_rateLimitLock = new();
    private static bool s_rateLimitedThisSession;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubTokenProvider _tokenProvider;

    public BlobClient(IHttpClientFactory httpClientFactory, IGitHubTokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
    }

    internal static void ResetAuthStateForTests()
    {
        lock (s_rateLimitLock)
        {
            s_rateLimitedThisSession = false;
        }
    }

    public async Task<RepoTree?> FetchTreeAsync(
        string owner,
        string repo,
        string? @ref,
        string? path,
        CancellationToken cancellationToken)
    {
        var ownerRepo = $"{owner}/{repo}";
        var branches = string.IsNullOrEmpty(@ref)
            ? new[] { "HEAD", "main", "master" }
            : new[] { @ref };

        var rateLimitedAtStart = IsRateLimited();

        if (rateLimitedAtStart)
        {
            var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                return null;
            }

            foreach (var branch in branches)
            {
                var result = await FetchTreeBranchAsync(ownerRepo, branch, token, cancellationToken).ConfigureAwait(false);
                if (result.Tree is not null)
                {
                    return ApplySubpath(result.Tree, path);
                }
            }

            return null;
        }

        var rateLimited = false;
        foreach (var branch in branches)
        {
            var result = await FetchTreeBranchAsync(ownerRepo, branch, token: null, cancellationToken).ConfigureAwait(false);
            if (result.Tree is not null)
            {
                return ApplySubpath(result.Tree, path);
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
        var fallbackToken = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (fallbackToken is null)
        {
            return null;
        }

        foreach (var branch in branches)
        {
            var result = await FetchTreeBranchAsync(ownerRepo, branch, fallbackToken, cancellationToken).ConfigureAwait(false);
            if (result.Tree is not null)
            {
                return ApplySubpath(result.Tree, path);
            }
        }

        return null;
    }

    public async Task<string?> FetchFileAsync(
        string owner,
        string repo,
        string path,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var branch = string.IsNullOrEmpty(@ref) ? "HEAD" : @ref;
        var url = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";

        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(s_fetchTimeout);

            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<BranchFetchResult> FetchTreeBranchAsync(
        string ownerRepo,
        string branch,
        string? token,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{ownerRepo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
        var client = _httpClientFactory.CreateClient(HttpClientName);

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

            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                var data = await JsonSerializer.DeserializeAsync(
                    stream,
                    JsonSourceGenerationContext.Default.GitHubTreeResponse,
                    timeoutCts.Token).ConfigureAwait(false);

                if (data is null)
                {
                    return new BranchFetchResult(null, RateLimited: false);
                }

                var tree = new RepoTree(data.Sha, branch, data.Tree ?? []);
                return new BranchFetchResult(tree, RateLimited: false);
            }

            var rateLimited = response.StatusCode == HttpStatusCode.Forbidden
                && GetRemaining(response) == "0";
            return new BranchFetchResult(null, rateLimited);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BranchFetchResult(null, RateLimited: false);
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

    private static RepoTree ApplySubpath(RepoTree tree, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return tree;
        }

        var prefix = path.EndsWith('/') ? path : path + "/";
        var filtered = tree.Tree
            .Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal) || e.Path == path)
            .ToList();

        return new RepoTree(tree.Sha, tree.Branch, filtered);
    }

    private static bool IsRateLimited()
    {
        lock (s_rateLimitLock)
        {
            return s_rateLimitedThisSession;
        }
    }

    private static void MarkRateLimited()
    {
        lock (s_rateLimitLock)
        {
            s_rateLimitedThisSession = true;
        }
    }

    private readonly record struct BranchFetchResult(RepoTree? Tree, bool RateLimited);
}
