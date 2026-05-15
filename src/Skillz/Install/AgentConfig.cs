namespace Skillz.Install;

internal sealed record AgentConfig(
    string Name,
    string DisplayName,
    string SkillsDir,
    string? GlobalSkillsDir,
    bool ShowInUniversalList = true);
