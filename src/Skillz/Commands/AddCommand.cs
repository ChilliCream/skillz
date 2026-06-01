using System.CommandLine;
using Skillz.Interaction;

namespace Skillz.Commands;

internal sealed class AddCommand(IInteractionService interaction, AddCommandExecutor executor)
    : BaseCommand("add", "Add a skill from a source")
{
    private readonly Argument<string?> _sourceArgument = new("source")
    {
        Description = "Source to fetch skills from (e.g., owner/repo, URL, local path)",
        Arity = ArgumentArity.ZeroOrOne
    };

    private readonly Option<bool> _globalOption = new(CommonOptionNames.Global, "-g")
    {
        Description = "Install globally"
    };

    private readonly Option<string[]> _agentOption = new(CommonOptionNames.Agent, "-a")
    {
        Description = "Target agent(s)",
        AllowMultipleArgumentsPerToken = true
    };

    private readonly Option<string[]> _skillOption = new(CommonOptionNames.Skill, "-s")
    {
        Description = "Skill name filter(s)",
        AllowMultipleArgumentsPerToken = true
    };

    private readonly Option<bool> _yesOption = new(CommonOptionNames.Yes, "-y")
    {
        Description = "Skip prompts (non-interactive)"
    };

    private readonly Option<bool> _allOption = new(CommonOptionNames.All)
    {
        Description = "Install all skills to all agents"
    };

    private readonly Option<bool> _copyOption = new(CommonOptionNames.Copy)
    {
        Description = "Copy instead of symlinking"
    };

    private readonly Option<bool> _fullDepthOption = new(CommonOptionNames.FullDepth)
    {
        Description = "Full-depth clone"
    };

    private readonly Option<bool> _listOption = new(CommonOptionNames.List, "-l")
    {
        Description = "List available skills without installing"
    };

    protected override void Configure()
    {
        Arguments.Add(_sourceArgument);
        Options.Add(_globalOption);
        Options.Add(_agentOption);
        Options.Add(_skillOption);
        Options.Add(_yesOption);
        Options.Add(_allOption);
        Options.Add(_copyOption);
        Options.Add(_fullDepthOption);
        Options.Add(_listOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        var options = ParseOptions(parseResult);

        if (string.IsNullOrEmpty(options.Source))
        {
            interaction.WriteError("Missing required argument: source");
            interaction.WriteLine("Usage: skillz add <source> [options]");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        return await executor.RunAsync(options, cancellationToken);
    }

    private AddCommandOptions ParseOptions(ParseResult parseResult)
    {
        var source = parseResult.GetValue(_sourceArgument);
        // System.CommandLine may pass "--" literally as the source value
        if (source == "--")
        {
            source = null;
        }
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? [];
        var skills = parseResult.GetValue(_skillOption) ?? [];
        var yes = parseResult.GetValue(_yesOption);
        var all = parseResult.GetValue(_allOption);
        var copy = parseResult.GetValue(_copyOption);
        var fullDepth = parseResult.GetValue(_fullDepthOption);
        var list = parseResult.GetValue(_listOption);

        if (all)
        {
            skills = ["*"];
            agents = ["*"];
            yes = true;
        }

        return new AddCommandOptions(source, global, [.. agents], [.. skills], yes, all, copy, fullDepth, list);
    }
}
