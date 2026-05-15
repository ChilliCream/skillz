using System.Text.Json.Serialization;

namespace Skillz.Net;

internal sealed class SearchSkill
{
    public SearchSkill(string name, string slug, string source, int installs)
    {
        Name = name;
        Slug = slug;
        Source = source;
        Installs = installs;
    }

    public string Name { get; }

    public string Slug { get; }

    public string Source { get; }

    public int Installs { get; }
}

internal sealed class SearchApiSkill
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("installs")]
    public int Installs { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

internal sealed class SearchApiResponse
{
    [JsonPropertyName("skills")]
    public List<SearchApiSkill>? Skills { get; set; }
}
