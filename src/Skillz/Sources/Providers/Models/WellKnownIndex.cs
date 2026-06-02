using System.Text.Json.Serialization;

namespace Skillz.Sources.Providers;

internal sealed class WellKnownIndex
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("skills")]
    public List<WellKnownIndexEntry>? Skills { get; set; }
}
