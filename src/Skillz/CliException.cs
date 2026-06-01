namespace Skillz;

internal class CliException(int exitCode, string message, string? title = null, string? hint = null)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public string? Title { get; } = title;

    public string? Hint { get; } = hint;
}
