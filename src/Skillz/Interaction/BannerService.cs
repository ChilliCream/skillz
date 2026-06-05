using System.Globalization;
using System.Text;
using Skillz.Install;
using Spectre.Console;

namespace Skillz.Interaction;

internal sealed class BannerService(
    IInteractionService interaction,
    CliExecutionContext context,
    AgentEnvironment agentEnvironment)
{
    private static readonly string[] s_logoLines =
    [
        "  /$$$$$$  /$$       /$$ /$$ /$$          ",
        " /$$__  $$| $$      |__/| $$| $$          ",
        "| $$  \\__/| $$   /$$ /$$| $$| $$ /$$$$$$$$",
        "|  $$$$$$ | $$  /$$/| $$| $$| $$|____ /$$/",
        " \\____  $$| $$$$$$/ | $$| $$| $$   /$$$$/ ",
        " /$$  \\ $$| $$_  $$ | $$| $$| $$  /$$__/  ",
        "|  $$$$$$/| $$ \\  $$| $$| $$| $$ /$$$$$$$$",
        " \\______/ |__/  \\__/|__/|__/|__/|________/"
    ];

    // ChilliCream's warm brand gradient (pink -> coral -> orange), swept left-to-right across the logo.
    private static readonly (int R, int G, int B)[] s_gradient =
    [
        (0xF6, 0x1D, 0x6E),
        (0xFB, 0x52, 0x2E),
        (0xFF, 0xA5, 0x2B)
    ];

    private static readonly int s_logoWidth = s_logoLines.Max(line => line.Length);

    public void ShowLogo()
    {
        if (ShouldSkip())
        {
            return;
        }

        interaction.WriteLine();
        foreach (var line in s_logoLines)
        {
            var builder = new StringBuilder(line.Length * 18);
            for (var column = 0; column < line.Length; column++)
            {
                var glyph = line[column];
                if (glyph == ' ')
                {
                    builder.Append(' ');
                    continue;
                }

                var (r, g, b) = GradientColor((double)column / (s_logoWidth - 1));
                builder.Append(CultureInfo.InvariantCulture, $"[#{r:x2}{g:x2}{b:x2}]{glyph}[/]");
            }

            interaction.WriteMarkupLine(builder.ToString());
        }
    }

    // Linearly interpolates between the gradient stops; fraction 0 maps to the first stop, 1 to the last.
    private static (int R, int G, int B) GradientColor(double fraction)
    {
        fraction = Math.Clamp(fraction, 0d, 1d);
        var segments = s_gradient.Length - 1;
        var scaled = fraction * segments;
        var index = Math.Min((int)scaled, segments - 1);
        var blend = scaled - index;

        var from = s_gradient[index];
        var to = s_gradient[index + 1];
        return (
            (int)Math.Round(from.R + ((to.R - from.R) * blend)),
            (int)Math.Round(from.G + ((to.G - from.G) * blend)),
            (int)Math.Round(from.B + ((to.B - from.B) * blend)));
    }

    public void ShowBanner()
    {
        if (ShouldSkip())
        {
            return;
        }

        ShowLogo();
        interaction.WriteLine();

        WriteCommandTable(
            ("[dim]$[/] [grey78]skillz add[/] [dim]<package>[/]", "Add a new skill"),
            ("[dim]$[/] [grey78]skillz remove[/]", "Remove installed skills"),
            ("[dim]$[/] [grey78]skillz list[/]", "List installed skills"),
            ("[dim]$[/] [grey78]skillz update[/]", "Update installed skills"),
            ("[dim]$[/] [grey78]skillz init[/] [dim][[name]][/]", "Create a new skill"));

        interaction.WriteLine();
        interaction.WriteMarkupLine("[dim]try:[/] [grey78]dnx skillz add[/] [#fb522e]chillicream/agent-skills[/]");
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
            ("[dim]$[/] [grey78]skillz add[/] [dim]chillicream/agent-skills[/]", ""),
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

        return agentEnvironment.IsRunningInsideAgent;
    }
}
