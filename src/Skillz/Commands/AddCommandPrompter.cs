using System.Collections.Immutable;
using System.Text;
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
    private static readonly ImmutableArray<(string Label, InstallMode Value)> s_installModeChoices =
    [
        ("Symlink (Recommended)", InstallMode.Symlink),
        ("Copy to all agents", InstallMode.Copy)
    ];

    private static readonly ImmutableArray<(string Label, bool Value)> s_globalScopeChoices =
    [
        ("Project (install in current directory)", false),
        ("Global (install in home directory)", true)
    ];

    private static readonly ImmutableHashSet<string> s_defaultSelection =
        ImmutableHashSet.Create(StringComparer.Ordinal, "claude-code", "opencode", "codex");

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

        // Named groups first (by plugin name), the unclaimed bucket last, skills alphabetical within
        // a group - mirroring how `skillz list` renders. Ordering up front means the prompt's GroupBy
        // preserves this order for both headers and their children.
        var ordered = skills
            .OrderBy(s => s.PluginName is null ? 1 : 0)
            .ThenBy(s => s.PluginName, StringComparer.Ordinal)
            .ThenBy(s => s.InstallName, StringComparer.Ordinal)
            .ToImmutableArray();

        // No plugin claims any skill - a lone "Other" header would just be noise, so use the flat picker.
        if (ordered.All(s => s.PluginName is null))
        {
            return await interaction.MultiSelectAsync(
                "Select skills to install",
                ordered.Select(static s => (s.Label, s)),
                cancellationToken);
        }

        return await interaction.MultiSelectGroupedAsync(
            "Select skills to install",
            ordered,
            static s => s.PluginName is { } pluginName ? pluginName.ToTitleCase() : "Other",
            static s => s.Label,
            cancellationToken);
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

        // Universal agents share .agents/skills, so installing to them is mandatory: their rows are mandatory.
        var ordered = available
            .OrderBy(a => registry.GetConfig(a).DisplayName, StringComparer.Ordinal)
            .ToList();

        var items = ordered
            .Select(a =>
            {
                var isUniversal = registry.IsUniversalAgent(a);
                return new SearchableItem<string>(
                    a,
                    $"{registry.GetConfig(a).DisplayName} ({a})",
                    Mandatory: isUniversal,
                    Note: isUniversal ? "universal · .agents" : null);
            })
            .ToList();

        // Pre-selection: last-used (filtered to what's available) if any; otherwise all universals plus
        // whichever common defaults are present.
        IReadOnlyList<string>? defaults = null;
        try
        {
            var lastUsed = await globalLock.GetLastSelectedAgentsAsync(cancellationToken);
            if (lastUsed is { Length: > 0 } lastUsedAgents)
            {
                var availableSet = new HashSet<string>(available, StringComparer.Ordinal);
                defaults = lastUsedAgents.Where(availableSet.Contains).ToList();
                if (defaults.Count == 0)
                {
                    defaults = null;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallow - best effort
        }

        defaults ??= ordered
            .Where(a => registry.IsUniversalAgent(a) || s_defaultSelection.Contains(a))
            .ToList();

        var selected = await interaction.SearchableMultiSelectAsync(
            "Which agents do you want to install to?",
            items,
            defaults,
            cancellationToken);

        // Save the selection for next time.
        if (selected.Length > 0)
        {
            try
            {
                await globalLock.SaveLastSelectedAgentsAsync(selected, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Swallow - best effort
            }
        }

        return selected;
    }

    public async Task<bool> SelectGlobalScopeAsync(CancellationToken cancellationToken)
    {
        return await interaction.SelectAsync("Installation scope", s_globalScopeChoices, cancellationToken);
    }

    public async Task<InstallMode> SelectInstallModeAsync(CancellationToken cancellationToken)
    {
        return await interaction.SelectAsync("Installation method", s_installModeChoices, cancellationToken);
    }

    public Task<bool> ConfirmInstallationAsync(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> agents,
        ImmutableArray<OverwriteTarget> overwrites,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("Install ").Append(skills.Length).Append(" skill(s) [");
        sb.AppendJoin(", ", skills.Select(s => s.InstallName));
        sb.Append("] to ").Append(agents.Length).Append(" agent(s) [");
        sb.AppendJoin(", ", agents);
        sb.Append(']');

        if (overwrites.Length > 0)
        {
            sb.AppendLine().Append("Existing installs will be overwritten:");
            foreach (var o in overwrites)
            {
                sb.AppendLine().Append("  - ").Append(o.SkillName).Append(": ").Append(o.DestinationPath);
            }
        }

        return interaction.ConfirmAsync(sb.ToString(), defaultValue: true, cancellationToken);
    }
}

file static class Extensions
{
    extension(ResolvedSkill skill)
    {
        public string Label
        {
            get
            {
                var hint = skill.Description.Length > 60 ? skill.Description[..57] + "..." : skill.Description;
                return $"{skill.InstallName} - {hint}";
            }
        }
    }
}
