using System.Collections.Immutable;
using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Commands;

internal interface IAddCommandPrompter
{
    Task<ImmutableArray<RemoteSkill>> SelectSkillsAsync(
        ImmutableArray<RemoteSkill> skills,
        CancellationToken cancellationToken = default);

    Task<ImmutableArray<string>> SelectAgentsAsync(
        ImmutableArray<string> available,
        bool global,
        CancellationToken cancellationToken = default);

    Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken = default);

    Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken = default);

    Task<bool> ConfirmInstallationAsync(
        ImmutableArray<RemoteSkill> skills,
        ImmutableArray<string> agents,
        ImmutableArray<OverwriteTarget> overwrites,
        CancellationToken cancellationToken = default);
}

internal sealed record OverwriteTarget(string SkillName, string DestinationPath);
