using System.CommandLine;
using Skillz.Interaction;
using Skillz.Views;
using Spectre.Console;

namespace Skillz.Commands;

internal abstract class BaseCommand : Command
{
    protected BaseCommand(
        IAnsiConsole console,
        string name,
        string? description = null)
        : base(name, description)
    {
        Output = console;

        Configure();

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                try
                {
                    return await ExecuteAsync(parseResult, cancellationToken);
                }
                catch (CliException ex)
                {
                    if (ex.Title is { } title)
                    {
                        console.WriteLine();
                        console.Write(ErrorView.Create(title, ex.Message, ex.Hint));
                    }
                    else
                    {
                        console.Error(ex.Message);
                    }

                    return ex.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    return ExitCodeConstants.Cancelled;
                }
            });
    }

    protected IAnsiConsole Output { get; }

    protected virtual void Configure() { }

    protected abstract Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken);
}
