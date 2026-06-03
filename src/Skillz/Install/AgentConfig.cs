namespace Skillz.Install;

internal sealed record AgentConfig(
    string Name,
    string DisplayName,
    string SkillsDirectory,
    string? GlobalSkillsDirectory,
    bool ShowInUniversalList = true);
