using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Net;

namespace Skillz.Commands;

internal sealed class FindCommand : BaseCommand
{
    public const string CommandName = "find";

    public static readonly IReadOnlyList<string> CommandAliases = new[] { "search", "f", "s" };

    private readonly IInteractionService _interaction;
    private readonly ISkillSearchClient _searchClient;
    private readonly IFindCommandPrompter _prompter;
    private readonly IAgentEnvironmentDetector _agentEnvironment;
    private readonly ConsoleEnvironment _consoleEnvironment;

    private readonly Argument<string[]> _queryArgument = new("query")
    {
        Description = "Optional search query (space-separated). When omitted, launches interactive search.",
        Arity = ArgumentArity.ZeroOrMore
    };

    public FindCommand(
        IInteractionService interaction,
        ISkillSearchClient searchClient,
        IFindCommandPrompter prompter,
        IAgentEnvironmentDetector agentEnvironment,
        ConsoleEnvironment consoleEnvironment)
        : base(CommandName, "Search for skills on skills.sh")
    {
        _interaction = interaction;
        _searchClient = searchClient;
        _prompter = prompter;
        _agentEnvironment = agentEnvironment;
        _consoleEnvironment = consoleEnvironment;

        foreach (var alias in CommandAliases)
        {
            Aliases.Add(alias);
        }

        Arguments.Add(_queryArgument);
    }

    protected override async Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var queryParts = parseResult.GetValue(_queryArgument) ?? Array.Empty<string>();
        var query = string.Join(' ', queryParts).Trim();

        if (!string.IsNullOrEmpty(query))
        {
            await RunNonInteractiveAsync(query, cancellationToken).ConfigureAwait(false);
            return new CommandResult.Success();
        }

        var isNonInteractive = _consoleEnvironment.IsInputRedirected;
        var inAgent = await _agentEnvironment.IsRunningInAgentAsync().ConfigureAwait(false);

        if (isNonInteractive || inAgent)
        {
            _interaction.WriteDim("Tip: if running in a coding agent, follow these steps:");
            _interaction.WriteDim("  1) skillz find [query]");
            _interaction.WriteDim("  2) skillz add <owner/repo@skill>");
            _interaction.WriteLine();
            _interaction.WriteDim("Usage: skillz find <query>");
            return new CommandResult.Success();
        }

        var selected = await _prompter.RunInteractiveSearchAsync(string.Empty, cancellationToken).ConfigureAwait(false);

        if (selected is null)
        {
            _interaction.WriteDim("Search cancelled");
            _interaction.WriteLine();
            return new CommandResult.Success();
        }

        var pkg = string.IsNullOrEmpty(selected.Source) ? selected.Slug : selected.Source;
        _interaction.WriteLine();
        _interaction.WriteMarkupLine($"Installing [bold]{Spectre.Console.Markup.Escape(selected.Name)}[/] from [dim]{Spectre.Console.Markup.Escape(pkg)}[/]...");
        _interaction.WriteLine();
        _interaction.WriteDim($"Run: skillz add {pkg} --skill {selected.Name}");
        _interaction.WriteLine();

        return new CommandResult.Success();
    }

    private async Task RunNonInteractiveAsync(string query, CancellationToken cancellationToken)
    {
        IReadOnlyList<SearchSkill> results;
        try
        {
            results = await _searchClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            results = Array.Empty<SearchSkill>();
        }

        if (results.Count == 0)
        {
            _interaction.WriteDim($"No skills found for \"{query}\"");
            return;
        }

        _interaction.WriteDim("Install with skillz add <owner/repo@skill>");
        _interaction.WriteLine();

        foreach (var skill in results.Take(6))
        {
            var pkg = string.IsNullOrEmpty(skill.Source) ? skill.Slug : skill.Source;
            var installs = FormatInstalls(skill.Installs);
            var installsBadge = string.IsNullOrEmpty(installs)
                ? string.Empty
                : $" [cyan]{Spectre.Console.Markup.Escape(installs)}[/]";
            _interaction.WriteMarkupLine($"[grey85]{Spectre.Console.Markup.Escape(pkg)}@{Spectre.Console.Markup.Escape(skill.Name)}[/]{installsBadge}");
            _interaction.WriteDim($"  https://skills.sh/{skill.Slug}");
            _interaction.WriteLine();
        }
    }

    private static string FormatInstalls(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        if (count >= 1_000_000)
        {
            return $"{(count / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}M installs";
        }

        if (count >= 1_000)
        {
            return $"{(count / 1_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}K installs";
        }

        return count == 1 ? "1 install" : $"{count} installs";
    }
}
