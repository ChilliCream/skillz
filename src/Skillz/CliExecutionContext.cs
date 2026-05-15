using System.CommandLine;

namespace Skillz;

internal sealed class CliExecutionContext
{
    public Command? CurrentCommand { get; set; }

    public bool IsJsonOutput { get; set; }
}
