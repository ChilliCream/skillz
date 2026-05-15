using System.Text.Json;
using Skillz.Skills;

namespace Skillz.Net;

internal sealed class SkillSearchClient : ISkillSearchClient
{
    internal const string HttpClientName = "Skillz.Search";

    private static readonly TimeSpan s_fetchTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;

    public SkillSearchClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<SearchSkill>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SKILLS_API_URL");
        if (string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = "https://skills.sh";
        }

        var url = $"{baseUrl}/api/search?q={Uri.EscapeDataString(query)}&limit=10";

        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(s_fetchTimeout);

            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            var data = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.SearchApiResponse,
                timeoutCts.Token).ConfigureAwait(false);

            if (data?.Skills is null)
            {
                return [];
            }

            return data.Skills
                .Select(s => new SearchSkill(
                    TerminalSanitizer.SanitizeMetadata(s.Name),
                    TerminalSanitizer.SanitizeMetadata(s.Id),
                    TerminalSanitizer.SanitizeMetadata(s.Source ?? string.Empty),
                    s.Installs))
                .OrderByDescending(s => s.Installs)
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return [];
        }
    }
}
