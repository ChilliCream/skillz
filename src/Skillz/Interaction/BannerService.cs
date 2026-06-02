using Skillz.Install;
using Spectre.Console;

namespace Skillz.Interaction;

internal sealed class BannerService(
    IInteractionService interaction,
    CliExecutionContext context,
    AgentEnvironment agentEnvironment)
{
    private static readonly string[] s_logoLines =
    {
        "███████╗██╗  ██╗██╗██╗     ██╗     ███████╗",
        "██╔════╝██║ ██╔╝██║██║     ██║     ╚══███╔╝",
        "███████╗█████╔╝ ██║██║     ██║       ███╔╝ ",
        "╚════██║██╔═██╗ ██║██║     ██║      ███╔╝  ",
        "███████║██║  ██╗██║███████╗███████╗███████╗",
        "╚══════╝╚═╝  ╚═╝╚═╝╚══════╝╚══════╝╚══════╝"
    };

    private static readonly string[] s_grays = { "grey78", "grey74", "grey69", "grey62", "grey58", "grey50" };

    public void ShowLogo()
    {
        if (ShouldSkip())
        {
            return;
        }

        interaction.WriteLine();
        for (var i = 0; i < s_logoLines.Length; i++)
        {
            interaction.WriteMarkupLine($"[{s_grays[i]}]{s_logoLines[i]}[/]");
        }
    }

    public void ShowBanner()
    {
        if (ShouldSkip())
        {
            return;
        }

        ShowLogo();
        interaction.WriteLine();
        interaction.WriteDim("The open agent skills ecosystem");
        interaction.WriteLine();

        WriteCommandTable(
            ("[dim]$[/] [grey78]skillz add[/] [dim]<package>[/]", "Add a new skill"),
            ("[dim]$[/] [grey78]skillz remove[/]", "Remove installed skills"),
            ("[dim]$[/] [grey78]skillz list[/]", "List installed skills"),
            ("[dim]$[/] [grey78]skillz update[/]", "Update installed skills"),
            ("[dim]$[/] [grey78]skillz init[/] [dim][[name]][/]", "Create a new skill"));

        interaction.WriteLine();
    }

    public void ShowCuratedHelp()
    {
        interaction.WriteLine();
        interaction.WriteMarkupLine("[bold]Usage:[/] [grey78]skillz[/] [dim]<command>[/] [dim][[options]][/]");
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Commands[/]");
        WriteCommandTable(
            ("[grey78]add[/] [dim]<source>[/]", "Add a skill from a source"),
            ("[grey78]remove[/] [dim][[names…]][/]", "Remove installed skills"),
            ("[grey78]list[/]", "List installed skills"),
            ("[grey78]update[/] [dim][[names…]][/]", "Check for and apply updates"),
            ("[grey78]init[/] [dim][[name]][/]", "Scaffold a new SKILL.md"));
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Options[/]");
        WriteCommandTable(
            ("[grey78]-g, --global[/]", "Operate on global scope"),
            ("[grey78]-a, --agent <list>[/]", "Target specific agent(s)"),
            ("[grey78]-y, --yes[/]", "Skip prompts (non-interactive)"),
            ("[grey78]-h, --help[/]", "Show help for a command"));
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Examples[/]");
        WriteCommandTable(
            ("[dim]$[/] [grey78]skillz add[/] [dim]vercel-labs/agent-skills[/]", ""),
            ("[dim]$[/] [grey78]skillz add[/] [dim]owner/repo@skill-name -y[/]", ""),
            ("[dim]$[/] [grey78]skillz list[/] [dim]-g --json[/]", ""),
            ("[dim]$[/] [grey78]skillz update -y[/]", ""),
            ("[dim]$[/] [grey78]skillz init[/] [dim]my-skill[/]", ""));
        interaction.WriteLine();

        interaction.WriteDim("Use 'skillz <command> --help' for detailed command-specific help.");
        interaction.WriteLine();
    }

    private void WriteCommandTable(params (string Command, string Description)[] rows)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadLeft(2).PadRight(4));
        grid.AddColumn(new GridColumn().PadRight(0));

        foreach (var (command, description) in rows)
        {
            grid.AddRow(command, description.Length == 0 ? string.Empty : $"[dim]{description}[/]");
        }

        interaction.WriteRenderable(grid);
    }

    private bool ShouldSkip()
    {
        if (context.IsJsonOutput)
        {
            return true;
        }

        return agentEnvironment.CurrentAgent.IsAgent;
    }
}
