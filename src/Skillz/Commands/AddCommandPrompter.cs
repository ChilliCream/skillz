using Skillz.Install;
using Skillz.Interaction;
using Skillz.Skills;

namespace Skillz.Commands;

internal sealed class AddCommandPrompter : IAddCommandPrompter
{
    private readonly IInteractionService _interaction;
    private readonly IAgentRegistry _registry;

    public AddCommandPrompter(IInteractionService interaction, IAgentRegistry registry)
    {
        _interaction = interaction;
        _registry = registry;
    }

    public async Task<IReadOnlyList<RemoteSkill>> SelectSkillsAsync(
        IReadOnlyList<RemoteSkill> skills,
        CancellationToken cancellationToken = default)
    {
        if (skills.Count == 0)
        {
            return Array.Empty<RemoteSkill>();
        }

        if (skills.Count == 1)
        {
            return skills;
        }

        var choices = skills.Select(s =>
        {
            var hint = s.Description.Length > 60
                ? s.Description[..57] + "..."
                : s.Description;
            return ($"{s.InstallName} - {hint}", s);
        });

        return await _interaction.MultiSelectAsync(
            "Select skills to install",
            choices,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SelectAgentsAsync(
        IReadOnlyList<string> available,
        bool global,
        CancellationToken cancellationToken = default)
    {
        if (available.Count == 0)
        {
            return Array.Empty<string>();
        }

        var choices = available.Select(a =>
        {
            var config = _registry.GetConfig(a);
            return ($"{config.DisplayName} ({a})", a);
        });

        return await _interaction.MultiSelectAsync(
            "Which agents do you want to install to?",
            choices,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken = default)
    {
        var choices = new (string Label, bool Value)[]
        {
            ("Project (install in current directory)", false),
            ("Global (install in home directory)", true)
        };

        return await _interaction.SelectAsync(
            "Installation scope",
            choices,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken = default)
    {
        var choices = new (string Label, InstallMode Value)[]
        {
            ("Symlink (Recommended)", InstallMode.Symlink),
            ("Copy to all agents", InstallMode.Copy)
        };

        return await _interaction.SelectAsync(
            "Installation method",
            choices,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ConfirmInstallationAsync(
        IReadOnlyList<RemoteSkill> skills,
        IReadOnlyList<string> agents,
        CancellationToken cancellationToken = default)
    {
        var skillNames = string.Join(", ", skills.Select(s => s.InstallName));
        var agentNames = string.Join(", ", agents);
        var message = $"Install {skills.Count} skill(s) [{skillNames}] to {agents.Count} agent(s) [{agentNames}]?";

        return _interaction.ConfirmAsync(message, defaultValue: true, cancellationToken);
    }
}
