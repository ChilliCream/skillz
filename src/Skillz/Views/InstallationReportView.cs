using System.Collections.Immutable;
using System.Text;
using Skillz.Install;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// Renders the outcome of an install: a summary panel (canonical store, universal/linked agents,
/// overwrites), the list of installed skills, and a closing reminder - plus a failure panel for any
/// skills that did not install. The registry and installer are passed to the factory to resolve agent
/// display names and canonical paths; the view itself holds no services.
/// </summary>
internal sealed class InstallationReportView : View
{
    private const string DoneReminder = "Done!  Review skills before use; they run with full agent permissions.";

    private readonly InstallReport _report;
    private readonly AgentRegistry _registry;
    private readonly ISkillInstaller _installer;

    private InstallationReportView(InstallReport report, AgentRegistry registry, ISkillInstaller installer)
    {
        _report = report;
        _registry = registry;
        _installer = installer;
    }

    public static InstallationReportView Create(
        InstallReport report,
        AgentRegistry registry,
        ISkillInstaller installer)
        => new(report, registry, installer);

    protected override IRenderable Build()
    {
        var blocks = new List<IRenderable>();

        if (_report.Successful.Length > 0)
        {
            AppendSuccess(blocks);
        }

        if (_report.Failed.Length > 0)
        {
            AppendFailure(blocks, _report.Failed);
        }

        return new Stacked([.. blocks]);
    }

    private void AppendSuccess(List<IRenderable> blocks)
    {
        var targetAgents = _report.TargetAgents;
        var successful = _report.Successful;
        var existingSkills = _report.ExistingSkills;
        var installGlobally = _report.InstallGlobally;
        var installMode = _report.InstallMode;

        var skillNames = successful.Select(r => r.SkillName).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var universals = targetAgents.Where(_registry.IsUniversalAgent).ToList();
        var linked = targetAgents.Where(a => !_registry.IsUniversalAgent(a)).ToList();
        var overwrites = skillNames.Where(existingSkills.Contains).ToList();
        var linkedLabel = installMode == InstallMode.Copy ? "Copied:" : "Symlinked:";

        // The canonical store is only materialized when skills are symlinked back to it; Copy mode
        // writes each agent directory directly and never touches it. Drive the report off the paths
        // the installer actually wrote (CanonicalPath is populated only when the store was created)
        // so we never advertise a "Canonical:" location that does not exist on disk.
        var canonicalWritten = successful.Any(r => !string.IsNullOrEmpty(r.Result.CanonicalPath));

        var summary = new StringBuilder();
        if (canonicalWritten)
        {
            var canonical =
                skillNames.Length == 1
                    ? _installer.GetCanonicalPath(skillNames[0], installGlobally)
                    : _installer.GetCanonicalSkillsDirectory(installGlobally);
            summary.AppendLine($"[bold]Canonical:[/] [dim]{Markup.Escape(canonical)}[/]");
        }
        if (universals.Count > 0)
        {
            summary.AppendLine($"[bold]Universal:[/]  {Markup.Escape(universals.Select(GetAgentDisplay).Join(", "))}");
        }
        if (linked.Count > 0)
        {
            summary.AppendLine($"[bold]{linkedLabel}[/]  {Markup.Escape(linked.Select(GetAgentDisplay).Join(", "))}");
        }
        if (overwrites.Count > 0)
        {
            summary.AppendLine($"[yellow]Overwrites:[/] {Markup.Escape(overwrites.Join(", "))}");
        }

        var installed = new StringBuilder();
        foreach (var skillName in skillNames)
        {
            var firstPath =
                successful
                    .Where(r => r.SkillName == skillName)
                    .Select(r => r.Result.Path)
                    .FirstOrDefault(p => !string.IsNullOrEmpty(p))
                ?? string.Empty;
            installed.AppendLine($"[green]✓[/] {Markup.Escape(skillName)}");
            installed.AppendLine($"  [dim]→ {Markup.Escape(firstPath)}[/]");
        }

        // Leading blank, the two panels back-to-back, a blank, then the reminder - mirroring the
        // original console.WriteLine()/Write(panel) sequence so the rendered output is unchanged.
        blocks.Add(BlankLine.Instance);
        blocks.Add(
            new Panel(new Markup(summary.ToString().TrimEnd()))
                .Header("[bold]Installation Summary[/]")
                .BorderColor(Color.Cyan1)
                .Expand());
        blocks.Add(
            new Panel(new Markup(installed.ToString().TrimEnd()))
                .Header($"[bold green]Installed {skillNames.Length} skill(s)[/]")
                .BorderColor(Color.Green)
                .Expand());
        blocks.Add(BlankLine.Instance);
        blocks.Add(new Markup($"[yellow]{Markup.Escape(DoneReminder)}[/]\n"));
    }

    private void AppendFailure(List<IRenderable> blocks, ImmutableArray<InstallEntry> failed)
    {
        var rows = new Rows(
            failed.Select(entry =>
            {
                var error = string.IsNullOrEmpty(entry.Result.Error) ? "unknown error" : entry.Result.Error;
                return new Markup(
                    $"[red]✗[/] {Markup.Escape(entry.SkillName)} → "
                        + $"{Markup.Escape(GetAgentDisplay(entry.AgentType))}: {Markup.Escape(error)}");
            }));

        blocks.Add(BlankLine.Instance);
        blocks.Add(
            new Panel(rows)
                .Header($"[bold red]Installation failed for {failed.Length} skill(s)[/]")
                .BorderColor(Color.Red)
                .Expand());
    }

    private string GetAgentDisplay(string agentType) => _registry.GetConfig(agentType).DisplayName;
}
