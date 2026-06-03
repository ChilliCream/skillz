using System.Collections.Immutable;
using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Commands;

/// <summary>
/// Drives the interactive prompts for the <c>add</c> command (which skills, which agents,
/// scope, install mode, and final confirmation), isolating console interaction so the
/// executor's flow can be tested without a TTY.
/// </summary>
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
