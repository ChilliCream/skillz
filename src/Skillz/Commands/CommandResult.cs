namespace Skillz.Commands;

internal abstract record CommandResult
{
    private CommandResult() { }

    public abstract int ExitCode { get; }

    public sealed record Success : CommandResult
    {
        public override int ExitCode => ExitCodeConstants.Success;
    }

    public sealed record Failure(int Code, string? Message = null) : CommandResult
    {
        public override int ExitCode => Code;
    }

    public sealed record Cancelled : CommandResult
    {
        public override int ExitCode => ExitCodeConstants.Cancelled;
    }

    public sealed record DisplayHelp : CommandResult
    {
        public override int ExitCode => ExitCodeConstants.Success;
    }
}
