using System.Text.Json.Serialization;

namespace Skillz.Locking;

internal sealed class LocalSkillLockFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("skills")]
    public Dictionary<string, LocalSkillLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);
}
