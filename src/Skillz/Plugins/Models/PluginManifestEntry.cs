using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skillz.Plugins;

internal sealed class PluginManifestEntry
{
    [JsonPropertyName("source")]
    public JsonElement? Source { get; set; }

    [JsonPropertyName("skills")]
    public List<string>? Skills { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
