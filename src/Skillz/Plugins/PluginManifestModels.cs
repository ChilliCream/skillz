using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skillz.Plugins;

internal sealed class MarketplaceManifest
{
    [JsonPropertyName("metadata")]
    public MarketplaceMetadata? Metadata { get; set; }

    [JsonPropertyName("plugins")]
    public List<PluginManifestEntry>? Plugins { get; set; }
}

internal sealed class MarketplaceMetadata
{
    [JsonPropertyName("pluginRoot")]
    public string? PluginRoot { get; set; }
}

internal sealed class PluginManifestEntry
{
    [JsonPropertyName("source")]
    public JsonElement? Source { get; set; }

    [JsonPropertyName("skills")]
    public List<string>? Skills { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class SinglePluginManifest
{
    [JsonPropertyName("skills")]
    public List<string>? Skills { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
