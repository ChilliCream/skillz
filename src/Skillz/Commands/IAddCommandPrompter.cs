using System.Collections.Immutable;
using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Commands;

internal interface IAddCommandPrompter
{
    Task<ImmutableArray<ResolvedSkill>> SelectSkillsAsync(
        ImmutableArray<ResolvedSkill> skills,
        CancellationToken cancellationToken);

    Task<ImmutableArray<string>> SelectAgentsAsync(
        ImmutableArray<string> available,
        bool global,
        CancellationToken cancellationToken);

    Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken);

    Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken);

    Task<bool> ConfirmInstallationAsync(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> agents,
        ImmutableArray<OverwriteTarget> overwrites,
        CancellationToken cancellationToken);
}

internal sealed record OverwriteTarget(string SkillName, string DestinationPath);
