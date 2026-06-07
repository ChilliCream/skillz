using Skillz.Paths;
using Xunit;

namespace Skillz.Tests.Paths;

public class SafePathDisplayTests
{
    [Fact]
    public void AbbreviateForDisplay_Should_NotMangleSibling_When_PathSharesHomePrefixWithoutBoundary()
    {
        // Arrange: home is a string-prefix of the path but NOT a directory ancestor
        // (home=/home/bob, path=/home/bobby/...), so it must not be abbreviated to "~by/...".
        var sep = Path.DirectorySeparatorChar;
        var home = $"{sep}home{sep}bob";
        var path = $"{sep}home{sep}bobby{sep}skills{sep}alpha";

        // Act
        var result = SafePath.AbbreviateForDisplay(path, home, cwd: null);

        // Assert
        Assert.Equal(path, result);
    }

    [Fact]
    public void AbbreviateForDisplay_Should_NotMangleSibling_When_PathSharesCwdPrefixWithoutBoundary()
    {
        // Arrange
        var sep = Path.DirectorySeparatorChar;
        var cwd = $"{sep}work{sep}proj";
        var path = $"{sep}work{sep}project{sep}skills{sep}alpha";

        // Act
        var result = SafePath.AbbreviateForDisplay(path, home: null, cwd: cwd);

        // Assert
        Assert.Equal(path, result);
    }

    [Fact]
    public void AbbreviateForDisplay_Should_ShortenToTilde_When_PathIsWithinHome()
    {
        // Arrange
        var sep = Path.DirectorySeparatorChar;
        var home = $"{sep}home{sep}bob";
        var path = $"{home}{sep}skills{sep}alpha";

        // Act
        var result = SafePath.AbbreviateForDisplay(path, home, cwd: null);

        // Assert
        Assert.Equal($"~{sep}skills{sep}alpha", result);
    }

    [Fact]
    public void AbbreviateForDisplay_Should_ShortenToDot_When_PathIsWithinCwd()
    {
        // Arrange
        var sep = Path.DirectorySeparatorChar;
        var cwd = $"{sep}work{sep}proj";
        var path = $"{cwd}{sep}skills{sep}alpha";

        // Act
        var result = SafePath.AbbreviateForDisplay(path, home: null, cwd: cwd);

        // Assert
        Assert.Equal($".{sep}skills{sep}alpha", result);
    }
}
