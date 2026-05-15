namespace Skillz;

internal class CliException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
