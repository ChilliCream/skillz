using Skillz.Commands;
using Xunit;

namespace Skillz.Tests;

public class CommandResultTests
{
    [Fact]
    public void Success_Returns_Zero_ExitCode()
    {
        // Act
        var result = new CommandResult.Success();

        // Assert
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Failure_Returns_Specified_ExitCode()
    {
        // Act
        var result = new CommandResult.Failure(42, "error");

        // Assert
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void Cancelled_Returns_130()
    {
        // Act
        var result = new CommandResult.Cancelled();

        // Assert
        Assert.Equal(130, result.ExitCode);
    }
}
