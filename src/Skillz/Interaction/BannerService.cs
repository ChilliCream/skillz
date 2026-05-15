namespace Skillz.Interaction;

internal sealed class BannerService(IInteractionService interaction, ConsoleEnvironment environment, CliExecutionContext context)
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

    private static readonly string[] s_grays =
    {
        "grey78",
        "grey74",
        "grey69",
        "grey62",
        "grey58",
        "grey50"
    };

    public void ShowLogo()
    {
        if (ShouldSkip())
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
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz add[/] [dim]<package>[/]        [dim]Add a new skill[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz remove[/]               [dim]Remove installed skills[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz list[/]                 [dim]List installed skills[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz find[/] [dim][[query]][/]         [dim]Search for skills[/]");
        interaction.WriteLine();
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz update[/]               [dim]Update installed skills[/]");
        interaction.WriteMarkupLine("  [dim]$[/] [grey78]skillz init[/] [dim][[name]][/]          [dim]Create a new skill[/]");
        interaction.WriteLine();
        interaction.WriteDim("try: skillz add chillicream/agent-skills");
        interaction.WriteLine();
        interaction.WriteMarkupLine("Discover more skills at [grey78]https://skills.sh/[/]");
        interaction.WriteLine();
    }

    private bool ShouldSkip()
    {
        return context.IsJsonOutput || !environment.IsTty;
    }
}
