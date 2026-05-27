using Skillz.Install;

namespace Skillz.Interaction;

internal sealed class BannerService(
    IInteractionService interaction,
    CliExecutionContext context,
    IAgentEnvironmentDetector detector)
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

    public async Task ShowLogoAsync()
    {
        if (await ShouldSkipAsync().ConfigureAwait(false))
        {
            return;
        }

        interaction.WriteLine();
        for (var i = 0; i < s_logoLines.Length; i++)
        {
            var line = s_logoLines[i];
            var gray = s_grays[i];
            interaction.WriteMarkupLine($"[{gray}]{line}[/]");
        }
    }

    public async Task ShowBannerAsync()
    {
        if (await ShouldSkipAsync().ConfigureAwait(false))
        {
            return;
        }

        await ShowLogoAsync().ConfigureAwait(false);
        interaction.WriteLine();
        interaction.WriteDim("The open agent skills ecosystem");
        interaction.WriteLine();
        interaction.WriteMarkupLine(
            "  [dim]$[/] [grey78]skillz add[/] [dim]<package>[/]        [dim]Add a new skill[/]");
        interaction.WriteMarkupLine(
            "  [dim]$[/] [grey78]skillz remove[/]               [dim]Remove installed skills[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz list[/]                 [dim]List installed skills[/]");
        interaction.WriteLine();
        interaction.WriteMarkupLine(
            "  [dim]$[/] [grey78]skillz update[/]               [dim]Update installed skills[/]");
        interaction.WriteMarkupLine(
            "  [dim]$[/] [grey78]skillz init[/] [dim][[name]][/]          [dim]Create a new skill[/]");
        interaction.WriteLine();
    }

    public void ShowCuratedHelp()
    {
        interaction.WriteLine();
        interaction.WriteMarkupLine("[bold]Usage:[/] [grey78]skillz[/] [dim]<command>[/] [dim][[options]][/]");
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Manage Skills[/]");
        interaction.WriteMarkupLine("  [grey78]add[/] [dim]<source>[/]              Add a skill from a source");
        interaction.WriteMarkupLine("  [grey78]remove[/] [dim][[names…]][/]         Remove installed skills");
        interaction.WriteMarkupLine("  [grey78]list[/]                     List installed skills");
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Updates[/]");
        interaction.WriteMarkupLine("  [grey78]update[/] [dim][[names…]][/]         Check for and apply updates");
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Init[/]");
        interaction.WriteMarkupLine("  [grey78]init[/] [dim][[name]][/]             Scaffold a new SKILL.md");
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Common Options[/]");
        interaction.WriteMarkupLine("  [grey78]-g, --global[/]             Operate on global scope");
        interaction.WriteMarkupLine("  [grey78]-a, --agent <list>[/]       Target specific agent(s)");
        interaction.WriteMarkupLine("  [grey78]-y, --yes[/]                Skip prompts (non-interactive)");
        interaction.WriteMarkupLine("  [grey78]-h, --help[/]               Show help for a command");
        interaction.WriteLine();

        interaction.WriteMarkupLine("[bold]Examples[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz add[/] [dim]vercel-labs/agent-skills[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz add[/] [dim]owner/repo@skill-name -y[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz list[/] [dim]-g --json[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz update -y[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz init[/] [dim]my-skill[/]");
        interaction.WriteLine();

        interaction.WriteDim("Use 'skillz <command> --help' for detailed command-specific help.");
        interaction.WriteLine();
    }

    private async Task<bool> ShouldSkipAsync()
    {
        if (context.IsJsonOutput)
        {
            return true;
        }

        return await detector.IsRunningInAgentAsync().ConfigureAwait(false);
    }
}
