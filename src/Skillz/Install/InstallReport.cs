using System.Collections.Immutable;
using Skillz.Skills;
using Skillz.Sources;

namespace Skillz.Install;

internal sealed record InstallEntry(ResolvedSkill Skill, string AgentType, InstallResult Result)
{
    public string SkillName => Skill.InstallName;
}

// The install outcome handed one-directionally from the executor to the recorder and the report view:
// it carries the union of what both need to persist lock entries and render the summary.
internal sealed record InstallReport(
    SkillSource Source,
    ImmutableArray<string> TargetAgents,
    ImmutableArray<InstallEntry> Successful,
    ImmutableArray<InstallEntry> Failed,
    ImmutableHashSet<string> ExistingSkills,
    bool InstallGlobally,
    InstallMode InstallMode);
