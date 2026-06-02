namespace Skillz.Skills;

internal record RemoteSkill(
    string Name,
    string Description,
    string Content,
    string InstallName,
    string SourceUrl,
    string ProviderId,
    string SourceIdentifier,
    string? SkillPath = null,
    string? SourcePath = null,
    Dictionary<string, object>? Metadata = null,
    string? CleanupPath = null,
    string? PluginName = null);
