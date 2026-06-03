using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Skills;

public class SubpathValidatorTests
{
    [Theory]
    [InlineData("skills/my-skill")]
    [InlineData("path/to/skill")]
    [InlineData("src")]
    public void ValidateSubpath_Allows_Normal_Subpaths(string input)
    {
        // Act & Assert
        Assert.Equal(input, SubpathValidator.ValidateSubpath(input));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("../../etc/passwd")]
    [InlineData("skills/../../etc")]
    [InlineData("a/b/../../../etc")]
    public void ValidateSubpath_Rejects_DotDot_Segments(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => SubpathValidator.ValidateSubpath(input));

        // Assert
        Assert.Contains("Unsafe subpath", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..\\etc")]
    [InlineData("..\\..\\secret")]
    public void ValidateSubpath_Rejects_Backslash_Traversal(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => SubpathValidator.ValidateSubpath(input));

        // Assert
        Assert.Contains("Unsafe subpath", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("file.txt")]
    [InlineData("path/to/.config")]
    [InlineData("..skill")]
    [InlineData("skill..")]
    public void ValidateSubpath_Allows_Dots_That_Are_Not_Traversal(string input)
    {
        // Act & Assert
        Assert.Equal(input, SubpathValidator.ValidateSubpath(input));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/skills/my-skill")]
    [InlineData("\\windows\\system32")]
    public void ValidateSubpath_Should_Reject_When_SubpathIsAbsolute(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => SubpathValidator.ValidateSubpath(input));

        // Assert
        Assert.Contains("Unsafe subpath", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("skills/\u001bmy-skill")]
    [InlineData("skills/my\nskill")]
    [InlineData("skills/my\tskill")]
    public void ValidateSubpath_Should_Reject_When_SubpathContainsControlCharacter(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => SubpathValidator.ValidateSubpath(input));

        // Assert
        Assert.Contains("control character", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/tmp/repo", "skills")]
    [InlineData("/tmp/repo", "skills/my-skill")]
    [InlineData("/tmp/repo", "a/b/c")]
    public void IsSubpathSafe_Returns_True_For_Subpaths_Within_BasePath(string basePath, string subpath)
    {
        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe(basePath, subpath));
    }

    [Theory]
    [InlineData("/tmp/repo", "..")]
    [InlineData("/tmp/repo", "../etc")]
    [InlineData("/tmp/repo", "../../etc/passwd")]
    [InlineData("/tmp/repo", "skills/../../..")]
    public void IsSubpathSafe_Returns_False_For_Subpaths_Escaping_BasePath(string basePath, string subpath)
    {
        // Act & Assert
        Assert.False(SubpathValidator.IsSubpathSafe(basePath, subpath));
    }

    [Fact]
    public void IsSubpathSafe_Handles_Normalized_Traversal_Staying_Within()
    {
        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe("/tmp/repo", "skills/../other"));
    }

    [Theory]
    [InlineData("/tmp/repo", ".")]
    [InlineData("/tmp/repo", "skills/..")]
    public void IsSubpathSafe_Handles_Subpath_Resolving_To_BasePath(string basePath, string subpath)
    {
        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe(basePath, subpath));
    }
}
