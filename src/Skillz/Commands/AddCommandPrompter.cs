using System.Collections.Immutable;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Skills;

namespace Skillz.Commands;

internal sealed class AddCommandPrompter(
    IInteractionService interaction,
    AgentRegistry registry,
    IGlobalLockFile globalLock) : IAddCommandPrompter
{
    public async Task<ImmutableArray<ResolvedSkill>> SelectSkillsAsync(
        ImmutableArray<ResolvedSkill> skills,
        CancellationToken cancellationToken)
    {
        if (skills.Length == 0)
        {
            return [];
        }

        if (skills.Length == 1)
        {
            return skills;
        }

        // Sort by PluginName (nulls last) then InstallName
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

        return await interaction.MultiSelectAsync("Select skills to install", choices, cancellationToken);
    }

    public async Task<ImmutableArray<string>> SelectAgentsAsync(
        ImmutableArray<string> available,
        bool global,
        CancellationToken cancellationToken)
    {
        if (available.Length == 0)
        {
            return [];
        }

        // Try to get last-used agents as pre-selection defaults
        IReadOnlyList<string>? defaults = null;
        try
        {
            var lastUsed = await globalLock.GetLastSelectedAgentsAsync(cancellationToken);
            if (lastUsed is { Length: > 0 } lastUsedAgents)
            {
                defaults = lastUsedAgents.Where(a => available.Contains(a, StringComparer.Ordinal)).ToList();
                if (defaults.Count == 0)
                {
                    defaults = null;
                }
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
            var config = registry.GetConfig(a);
            return ($"{config.DisplayName} ({a})", a);
        });

        var selected = await interaction.MultiSelectAsync(
            "Which agents do you want to install to?",
            choices,
            cancellationToken);

        // Save selection for next time
        if (selected.Length > 0)
        {
            try
            {
                await globalLock.SaveLastSelectedAgentsAsync(selected, cancellationToken);
            }
            catch
            {
                // Swallow - best effort
            }
        }

        return selected;
    }

    public async Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken)
    {
        var choices = new (string Label, bool Value)[]
        {
            ("Project (install in current directory)", false),
            ("Global (install in home directory)", true)
        };

        return await interaction.SelectAsync("Installation scope", choices, cancellationToken);
    }

    public async Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken)
    {
        var choices = new (string Label, InstallMode Value)[]
        {
            ("Symlink (Recommended)", InstallMode.Symlink),
            ("Copy to all agents", InstallMode.Copy)
        };

        return await interaction.SelectAsync("Installation method", choices, cancellationToken);
    }

    public Task<bool> ConfirmInstallationAsync(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> agents,
        ImmutableArray<OverwriteTarget> overwrites,
        CancellationToken cancellationToken)
    {
        var skillNames = skills.Select(s => s.InstallName).Join(", ");
        var agentNames = agents.Join(", ");
        var message = $"Install {skills.Length} skill(s) [{skillNames}] to {agents.Length} agent(s) [{agentNames}]?";
        if (overwrites.Length > 0)
        {
            var targets = overwrites
                .Select(o => $"  - {o.SkillName}: {o.DestinationPath}")
                .Join(Environment.NewLine);
            message += $"{Environment.NewLine}Existing installs will be overwritten:{Environment.NewLine}{targets}";
        }

        return interaction.ConfirmAsync(message, defaultValue: true, cancellationToken);
    }
}
