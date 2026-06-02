using System.Text.Json.Serialization;

namespace Skillz.Locking;

internal sealed class SkillLockEntry
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("skillPath")]
    public string? SkillPath { get; set; }

    [JsonPropertyName("skillFolderHash")]
    public string SkillFolderHash { get; set; } = string.Empty;

    [JsonPropertyName("installedAt")]
    public string InstalledAt { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("pluginName")]
    public string? PluginName { get; set; }
}
