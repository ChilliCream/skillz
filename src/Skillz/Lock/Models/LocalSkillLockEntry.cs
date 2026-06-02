using System.Text.Json.Serialization;

namespace Skillz.Locking;

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
