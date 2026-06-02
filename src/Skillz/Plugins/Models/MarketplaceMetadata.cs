using System.Text.Json.Serialization;

namespace Skillz.Plugins;

internal sealed class MarketplaceMetadata
{
    [JsonPropertyName("pluginRoot")]
    public string? PluginRoot { get; set; }
}
