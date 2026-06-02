using System.Text.Json.Serialization;

namespace Skillz.Plugins;

internal sealed class MarketplaceManifest
{
    [JsonPropertyName("metadata")]
    public MarketplaceMetadata? Metadata { get; set; }

    [JsonPropertyName("plugins")]
    public List<PluginManifestEntry>? Plugins { get; set; }
}
