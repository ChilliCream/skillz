using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Skills;

namespace Skillz.Commands;

internal sealed class AddCommandPrompter : IAddCommandPrompter
{
    private readonly IInteractionService _interaction;
    private readonly IAgentRegistry _registry;
    private readonly IGlobalLockFile _globalLock;

    public AddCommandPrompter(IInteractionService interaction, IAgentRegistry registry, IGlobalLockFile globalLock)
    {
        _interaction = interaction;
        _registry = registry;
        _globalLock = globalLock;
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

        // Sort by PluginName (nulls last) then InstallName — matches TS add.ts:1181-1188
        var sorted = skills
            .OrderBy(s => s.PluginName is null ? 1 : 0)
            .ThenBy(s => s.PluginName, StringComparer.Ordinal)
            .ThenBy(s => s.InstallName, StringComparer.Ordinal)
            .ToList();

        var choices = sorted.Select(s =>
        {
            var hint = s.Description.Length > 60 ? s.Description[..57] + "..." : s.Description;
            return ($"{s.InstallName} - {hint}", s);
        });

        return await _interaction
            .MultiSelectAsync("Select skills to install", choices, cancellationToken)
            .ConfigureAwait(false);
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

        // Try to get last-used agents as pre-selection defaults
        IReadOnlyList<string>? defaults = null;
        try
        {
            var lastUsed = await _globalLock.GetLastSelectedAgentsAsync(cancellationToken).ConfigureAwait(false);
            if (lastUsed is { Count: > 0 })
            {
                defaults = lastUsed.Where(a => available.Contains(a, StringComparer.Ordinal)).ToList();
                if (defaults.Count == 0)
                    defaults = null;
            }
        }
        catch
        {
            // Swallow - best effort
        }

        // If no last-used, fall back to common defaults
        defaults ??= available.Where(a => a is "claude-code" or "opencode" or "codex").ToList();

        var choices = available.Select(a =>
        {
            var config = _registry.GetConfig(a);
            return ($"{config.DisplayName} ({a})", a);
        });

        var selected = await _interaction
            .MultiSelectAsync("Which agents do you want to install to?", choices, cancellationToken)
            .ConfigureAwait(false);

        // Save selection for next time
        if (selected.Count > 0)
        {
            try
            {
                await _globalLock.SaveLastSelectedAgentsAsync(selected, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Swallow - best effort
            }
        }

        return selected;
    }

    public async Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken = default)
    {
        var choices = new (string Label, bool Value)[]
        {
            ("Project (install in current directory)", false),
            ("Global (install in home directory)", true)
        };

        return await _interaction.SelectAsync("Installation scope", choices, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken = default)
    {
        var choices = new (string Label, InstallMode Value)[]
        {
            ("Symlink (Recommended)", InstallMode.Symlink),
            ("Copy to all agents", InstallMode.Copy)
        };

        return await _interaction.SelectAsync("Installation method", choices, cancellationToken).ConfigureAwait(false);
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
