namespace Skillz.Skills;

internal record Skill(
    string Name,
    string Description,
    string Path,
    string? RawContent = null,
    string? PluginName = null,
    Dictionary<string, object>? Metadata = null);
