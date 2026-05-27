using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Commands;

internal interface IAddCommandPrompter
{
    Task<IReadOnlyList<RemoteSkill>> SelectSkillsAsync(
        IReadOnlyList<RemoteSkill> skills,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SelectAgentsAsync(
        IReadOnlyList<string> available,
        bool global,
        CancellationToken cancellationToken = default);

    Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken = default);

    Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken = default);

    Task<bool> ConfirmInstallationAsync(
        IReadOnlyList<RemoteSkill> skills,
        IReadOnlyList<string> agents,
        CancellationToken cancellationToken = default);
}
