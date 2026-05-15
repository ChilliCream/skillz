using System.Text.Json.Serialization;

namespace Skillz.Sources.Providers;

internal sealed class WellKnownIndex
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("skills")]
    public List<WellKnownIndexEntry>? Skills { get; set; }
}

internal sealed class WellKnownIndexEntry
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}
