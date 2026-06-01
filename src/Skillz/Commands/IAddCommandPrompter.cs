using System.Collections.Immutable;
using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Commands;

internal interface IAddCommandPrompter
{
    Task<ImmutableArray<RemoteSkill>> SelectSkillsAsync(
        ImmutableArray<RemoteSkill> skills,
        CancellationToken cancellationToken);

    Task<ImmutableArray<string>> SelectAgentsAsync(
        ImmutableArray<string> available,
        bool global,
        CancellationToken cancellationToken);

    Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken);

    Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken);

    Task<bool> ConfirmInstallationAsync(
        ImmutableArray<RemoteSkill> skills,
        ImmutableArray<string> agents,
        ImmutableArray<OverwriteTarget> overwrites,
        CancellationToken cancellationToken);
}

internal sealed record OverwriteTarget(string SkillName, string DestinationPath);
