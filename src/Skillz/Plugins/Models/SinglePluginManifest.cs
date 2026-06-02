using System.Text.Json.Serialization;

namespace Skillz.Plugins;

internal sealed class SinglePluginManifest
{
    [JsonPropertyName("skills")]
    public List<string>? Skills { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
