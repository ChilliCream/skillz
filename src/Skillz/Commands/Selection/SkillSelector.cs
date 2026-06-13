using System.Collections.Immutable;
using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Skillz.Interaction.Prompts;
using Skillz.Skills;
using Skillz.Utils;
using IAnsiConsole = Spectre.Console.IAnsiConsole;

namespace Skillz.Commands.Selection;

internal sealed class SkillSelector(IAnsiConsole console) : ISkillSelector
{
    public async Task<ImmutableArray<ResolvedSkill>> SelectAsync(
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

        // Named groups first (by plugin name), the unclaimed bucket last, skills alphabetical within a
        // group - mirroring how `skillz list` renders. Ordering up front means the prompt's GroupBy
        // preserves this order for both headers and their children.
        var ordered = skills
            .OrderBy(s => s.PluginName is null ? 1 : 0)
            .ThenBy(s => s.PluginName, StringComparer.Ordinal)
            .ThenBy(s => s.InstallName, StringComparer.Ordinal)
            .ToImmutableArray();

        // No plugin claims any skill - a lone "Other" header would just be noise, so use the flat picker.
        // Otherwise group under the title-cased plugin name, or "Other" for the unclaimed.
        IPrompt<ImmutableArray<ResolvedSkill>> picker = ordered.All(static s => s.PluginName is null)
            ? new MultiSelectPrompt<ResolvedSkill>(
                "Select skills to install",
                ordered.Select(static s => (s.Label, s)))
            : new GroupedMultiSelectPrompt<ResolvedSkill>(
                "Select skills to install",
                ordered,
                static s => s.PluginName is { } pluginName ? pluginName.ToTitleCase() : "Other",
                static s => s.Label);

        // The multi-select list prompts also accept Vim-style j/k for down/up; selection is required.
        // WithDefault degrades to an empty selection when the console cannot drive the key loop (e.g.
        // output is redirected), so a piped run cancels gracefully instead of throwing from Spectre.
        return await picker
            .RequireNonEmpty()
            .WithDefault(ImmutableArray<ResolvedSkill>.Empty)
            .ShowAsync(new VimNavConsole(console), cancellationToken);
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
