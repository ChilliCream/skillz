using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;

namespace Skillz.Commands;

internal sealed class AddCommand : BaseCommand
{
    private readonly IServiceProvider _services;
    private readonly Argument<string?> _sourceArgument;
    private readonly Option<bool> _globalOption;
    private readonly Option<string[]> _agentOption;
    private readonly Option<string[]> _skillOption;
    private readonly Option<bool> _yesOption;
    private readonly Option<bool> _allOption;
    private readonly Option<bool> _copyOption;
    private readonly Option<bool> _fullDepthOption;
    private readonly Option<bool> _listOption;

    public AddCommand(IServiceProvider services)
        : base("add", "Add a skill from a source")
    {
        _services = services;

        _sourceArgument = new Argument<string?>("source")
        {
            Description = "Source to fetch skills from (e.g., owner/repo, URL, local path)",
            Arity = ArgumentArity.ZeroOrOne
        };
        Arguments.Add(_sourceArgument);

        _globalOption = new Option<bool>(CommonOptionNames.Global, "-g")
        {
            Description = "Install globally"
        };
        Options.Add(_globalOption);

        _agentOption = new Option<string[]>(CommonOptionNames.Agent, "-a")
        {
            Description = "Target agent(s)",
            AllowMultipleArgumentsPerToken = true
        };
        Options.Add(_agentOption);

        _skillOption = new Option<string[]>(CommonOptionNames.Skill, "-s")
        {
            Description = "Skill name filter(s)",
            AllowMultipleArgumentsPerToken = true
        };
        Options.Add(_skillOption);

        _yesOption = new Option<bool>(CommonOptionNames.Yes, "-y")
        {
            Description = "Skip prompts (non-interactive)"
        };
        Options.Add(_yesOption);

        _allOption = new Option<bool>(CommonOptionNames.All)
        {
            Description = "Install all skills to all agents"
        };
        Options.Add(_allOption);

        _copyOption = new Option<bool>(CommonOptionNames.Copy)
        {
            Description = "Copy instead of symlinking"
        };
        Options.Add(_copyOption);

        _fullDepthOption = new Option<bool>(CommonOptionNames.FullDepth)
        {
            Description = "Full-depth clone"
        };
        Options.Add(_fullDepthOption);

        _listOption = new Option<bool>(CommonOptionNames.List, "-l")
        {
            Description = "List available skills without installing"
        };
        Options.Add(_listOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var options = ParseOptions(parseResult);

        if (string.IsNullOrEmpty(options.Source))
        {
            var interaction = _services.GetService(typeof(IInteractionService)) as IInteractionService;
            interaction?.WriteError("Missing required argument: source");
            interaction?.WriteLine("Usage: skillz add <source> [options]");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        var executor = new AddCommandExecutor(_services);
        return await executor.RunAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private AddCommandOptions ParseOptions(ParseResult parseResult)
    {
        var source = parseResult.GetValue(_sourceArgument);
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? Array.Empty<string>();
        var skills = parseResult.GetValue(_skillOption) ?? Array.Empty<string>();
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

        return new AddCommandOptions(source, global, agents, skills, yes, all, copy, fullDepth, list);
    }
}
