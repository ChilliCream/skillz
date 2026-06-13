using System.Collections.Immutable;
using System.Text;
using Skillz.Skills;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// Lists the skills a source offers, grouped by plugin (named groups first, the unclaimed bucket last)
/// with a trailing hint about installing a subset. Rendered by the add command's <c>--list</c> path.
/// </summary>
internal sealed class SkillListView : View
{
    private const string Hint = "Use --skill <name> to install specific skills";

    private readonly ImmutableArray<ResolvedSkill> _skills;

    private SkillListView(ImmutableArray<ResolvedSkill> skills) => _skills = skills;

    public static SkillListView Create(ImmutableArray<ResolvedSkill> skills) => new(skills);

    protected override IRenderable Build()
    {
        var body = new StringBuilder();
        body.AppendLine("[bold]Available Skills[/]");

        var groups = _skills
            .GroupBy(s => s.PluginName, StringComparer.Ordinal)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        var first = true;
        foreach (var group in groups)
        {
            if (group.Key is { } pluginName)
            {
                if (!first)
                {
                    body.AppendLine();
                }
                body.AppendLine($"[bold]{Markup.Escape(pluginName.ToTitleCase())}[/]");
            }

            foreach (var skill in group.OrderBy(s => s.InstallName, StringComparer.Ordinal))
            {
                body.AppendLine($"  [cyan]{Markup.Escape(skill.InstallName)}[/]");
                body.AppendLine($"[dim]    {Markup.Escape(skill.Description)}[/]");
            }

            first = false;
        }

        return new Markup($"{body.ToString().TrimEnd()}\n\n[dim]{Markup.Escape(Hint)}[/]");
    }
}
