namespace Skillz.Commands;

internal abstract record CommandResult
{
    private CommandResult() { }

    public int ExitCode
        => this switch
        {
            Success => ExitCodeConstants.Success,
            Failure f => f.Code,
            Cancelled => ExitCodeConstants.Cancelled,
            DisplayHelp => ExitCodeConstants.Success,
            _ => ExitCodeConstants.Failure
        };

    public sealed record Success : CommandResult;

    public sealed record Failure(int Code, string? Message = null) : CommandResult;

    public sealed record Cancelled : CommandResult;

    public sealed record DisplayHelp : CommandResult;
}
