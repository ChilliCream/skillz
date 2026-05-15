namespace Skillz.Skills;

internal record Skill(
    string Name,
    string Description,
    string Path,
    string? RawContent = null,
    string? PluginName = null,
    Dictionary<string, object>? Metadata = null);

internal record RemoteSkill(
    string Name,
    string Description,
    string Content,
    string InstallName,
    string SourceUrl,
    string ProviderId,
    string SourceIdentifier,
    string? SkillPath = null,
    Dictionary<string, object>? Metadata = null);
