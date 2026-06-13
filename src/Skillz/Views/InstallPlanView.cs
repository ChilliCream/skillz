using System.Collections.Immutable;
using System.Text;
using Skillz.Skills;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// The plan shown before an interactive install confirm: which skills go to which agents, and any
/// existing installs that would be overwritten. Rendered as a panel, after which the command asks a
/// plain yes/no to proceed.
/// </summary>
internal sealed class InstallPlanView : View
{
    private readonly ImmutableArray<ResolvedSkill> _skills;
    private readonly ImmutableArray<string> _agents;
    private readonly IReadOnlyList<(string Skill, string Path)> _overwrites;

    private InstallPlanView(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> agents,
        IReadOnlyList<(string Skill, string Path)> overwrites)
    {
        _skills = skills;
        _agents = agents;
        _overwrites = overwrites;
    }

    public static InstallPlanView Create(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> agents,
        IReadOnlyList<(string Skill, string Path)> overwrites)
        => new(skills, agents, overwrites);

    protected override IRenderable Build()
    {
        var body = new StringBuilder();
        body.Append($"[bold]Install[/] {_skills.Length} skill(s): {Markup.Escape(_skills.Select(s => s.InstallName).Join(", "))}");
        body.Append($"\n[bold]To[/] {_agents.Length} agent(s): {Markup.Escape(_agents.Join(", "))}");

        if (_overwrites.Count > 0)
        {
            body.Append("\n\n[yellow]Existing installs will be overwritten:[/]");
            foreach (var (skill, path) in _overwrites)
            {
                // Dim the whole line so the "<skill>: <path>" text stays one contiguous run.
                body.Append($"\n[dim]  - {Markup.Escape(skill)}: {Markup.Escape(path)}[/]");
            }
        }

        return new Stacked(
            BlankLine.Instance,
            new Panel(new Markup(body.ToString()))
                .Header("[bold]Installation plan[/]")
                .BorderColor(Color.Cyan1)
                .Expand());
    }
}
