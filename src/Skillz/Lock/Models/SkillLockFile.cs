using System.Text.Json.Serialization;

namespace Skillz.Locking;

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
