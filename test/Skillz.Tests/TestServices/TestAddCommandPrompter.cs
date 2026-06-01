using System.Collections.Immutable;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Tests.TestServices;

internal sealed class TestAddCommandPrompter : IAddCommandPrompter
{
    public Func<IReadOnlyList<RemoteSkill>, IReadOnlyList<RemoteSkill>>? OnSelectSkills { get; set; }

    public Func<IReadOnlyList<string>, bool, IReadOnlyList<string>>? OnSelectAgents { get; set; }

    public Func<bool>? OnSelectGlobalScope { get; set; }

    public Func<InstallMode>? OnSelectInstallMode { get; set; }

    public Func<IReadOnlyList<RemoteSkill>, IReadOnlyList<string>, IReadOnlyList<OverwriteTarget>, bool>?
        OnConfirmInstallation { get; set; }

    public ImmutableArray<OverwriteTarget> LastOverwriteTargets { get; private set; } = [];

    public Task<ImmutableArray<RemoteSkill>> SelectSkillsAsync(
        ImmutableArray<RemoteSkill> skills,
        CancellationToken cancellationToken = default)
    {
        var result = OnSelectSkills is not null ? OnSelectSkills(skills) : skills;
        return Task.FromResult<ImmutableArray<RemoteSkill>>([.. result]);
    }

    public Task<ImmutableArray<string>> SelectAgentsAsync(
        ImmutableArray<string> available,
        bool global,
        CancellationToken cancellationToken = default)
    {
        var result = OnSelectAgents is not null ? OnSelectAgents(available, global) : available;
        return Task.FromResult<ImmutableArray<string>>([.. result]);
    }

    public Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken = default)
    {
        var result = OnSelectGlobalScope is not null && OnSelectGlobalScope();
        return Task.FromResult(result);
    }

    public Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken = default)
    {
        var result = OnSelectInstallMode is not null ? OnSelectInstallMode() : InstallMode.Symlink;
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmInstallationAsync(
        ImmutableArray<RemoteSkill> skills,
        ImmutableArray<string> agents,
        ImmutableArray<OverwriteTarget> overwrites,
        CancellationToken cancellationToken = default)
    {
        LastOverwriteTargets = overwrites;
        var result = OnConfirmInstallation is null || OnConfirmInstallation(skills, agents, overwrites);
        return Task.FromResult(result);
    }
}
