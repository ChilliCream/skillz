using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>One installed skill as the list view shows it: its name, a display-ready path, and the
/// resolved display names of the agents it is linked into.</summary>
internal sealed record InstalledSkillRow(string Name, string Path, IReadOnlyList<string> Agents);

/// <summary>
/// The <c>skillz list</c> table: a "&lt;scope&gt; Skills" heading over a Skill / Path / Agents grid.
/// Long agent lists are truncated to five plus a "+N more"; an unlinked skill reads "not linked".
/// </summary>
internal sealed class InstalledSkillsView : View
{
    private const int MaxAgentsShown = 5;

    private readonly string _scopeLabel;
    private readonly IReadOnlyList<InstalledSkillRow> _rows;

    private InstalledSkillsView(string scopeLabel, IReadOnlyList<InstalledSkillRow> rows)
    {
        _scopeLabel = scopeLabel;
        _rows = rows;
    }

    public static InstalledSkillsView Create(string scopeLabel, IReadOnlyList<InstalledSkillRow> rows)
        => new(scopeLabel, rows);

    protected override IRenderable Build()
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn().PadRight(0));
        grid.AddRow("[grey66]Skill[/]", "[grey66]Path[/]", "[grey66]Agents[/]");

        foreach (var row in _rows)
        {
            string agentCell;
            if (row.Agents.Count == 0)
            {
                agentCell = "[yellow]not linked[/]";
            }
            else
            {
                var display =
                    row.Agents.Count > MaxAgentsShown
                        ? row.Agents.Take(MaxAgentsShown).Join(", ") + $" +{row.Agents.Count - MaxAgentsShown} more"
                        : row.Agents.Join(", ");
                agentCell = $"[dim]{Markup.Escape(display)}[/]";
            }

            grid.AddRow(
                $"[cyan]{Markup.Escape(row.Name)}[/]",
                $"[dim]{Markup.Escape(row.Path)}[/]",
                agentCell);
        }

        return new Stacked(
            new Markup($"[bold]{Markup.Escape(_scopeLabel)} Skills[/]\n"),
            BlankLine.Instance,
            grid);
    }
}
