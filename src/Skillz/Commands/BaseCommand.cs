using System.CommandLine;
using System.CommandLine.Help;

namespace Skillz.Commands;

internal abstract class BaseCommand : Command
{
    protected BaseCommand(string name, string? description = null) : base(name, description)
    {
        Configure();

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                var result = await ExecuteAsync(parseResult, cancellationToken).ConfigureAwait(false);

                if (result is CommandResult.DisplayHelp)
                {
                    new HelpAction().Invoke(parseResult);
                    return ExitCodeConstants.Success;
                }

                if (result is CommandResult.Failure { Message: { } message }
                    && !string.IsNullOrWhiteSpace(message))
                {
                    Console.Error.WriteLine(message);
                }

                return result.ExitCode;
            });
    }

    protected virtual void Configure() { }

    protected abstract Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken);
}
