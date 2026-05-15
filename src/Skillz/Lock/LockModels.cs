using System.Text.Json.Serialization;

namespace Skillz.Lock;

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

internal sealed class SkillLockFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("skills")]
    public Dictionary<string, SkillLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("dismissed")]
    public Dictionary<string, bool>? Dismissed { get; set; }

    [JsonPropertyName("lastSelectedAgents")]
    public List<string>? LastSelectedAgents { get; set; }
}

internal sealed class LocalSkillLockEntry
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("skillPath")]
    public string? SkillPath { get; set; }

    [JsonPropertyName("computedHash")]
    public string ComputedHash { get; set; } = string.Empty;
}

internal sealed class LocalSkillLockFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("skills")]
    public Dictionary<string, LocalSkillLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);
}
