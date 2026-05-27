using Skillz.Commands;
using Xunit;

namespace Skillz.Tests;

public class CommandResultTests
{
    [Fact]
    public void Success_Returns_Zero_ExitCode()
    {
        var result = new CommandResult.Success();
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Failure_Returns_Specified_ExitCode()
    {
        var result = new CommandResult.Failure(42, "error");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void Cancelled_Returns_130()
    {
        var result = new CommandResult.Cancelled();
        Assert.Equal(130, result.ExitCode);
    }
}
