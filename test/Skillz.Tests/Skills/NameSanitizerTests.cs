using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Skills;

public class NameSanitizerTests
{
    [Theory]
    [InlineData("MySkill", "myskill")]
    [InlineData("UPPERCASE", "uppercase")]
    public void Converts_To_Lowercase(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("my skill", "my-skill")]
    [InlineData("Convex Best Practices", "convex-best-practices")]
    public void Replaces_Spaces_With_Hyphens(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Fact]
    public void Replaces_Multiple_Spaces_With_Single_Hyphen()
    {
        // Act & Assert
        Assert.Equal("my-skill", NameSanitizer.SanitizeName("my   skill"));
    }

    [Theory]
    [InlineData("bun.sh", "bun.sh")]
    [InlineData("my_skill", "my_skill")]
    [InlineData("skill.v2_beta", "skill.v2_beta")]
    public void Preserves_Dots_And_Underscores(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("skill123", "skill123")]
    [InlineData("v2.0", "v2.0")]
    public void Preserves_Numbers(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("skill@name", "skill-name")]
    [InlineData("skill#name", "skill-name")]
    [InlineData("skill$name", "skill-name")]
    [InlineData("skill!name", "skill-name")]
    public void Replaces_Special_Characters_With_Hyphens(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("skill@#$name", "skill-name")]
    [InlineData("a!!!b", "a-b")]
    public void Collapses_Multiple_Special_Chars_Into_Single_Hyphen(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("../etc/passwd", "etc-passwd")]
    [InlineData("../../secret", "secret")]
    public void Prevents_Path_Traversal_With_DotDot(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Fact]
    public void Prevents_Path_Traversal_With_Backslashes()
    {
        // Act & Assert
        Assert.Equal("secret", NameSanitizer.SanitizeName("..\\..\\secret"));
    }

    [Theory]
    [InlineData("/etc/passwd", "etc-passwd")]
    [InlineData("C:\\Windows\\System32", "c-windows-system32")]
    public void Handles_Absolute_Paths(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData(".hidden", "hidden")]
    [InlineData("..hidden", "hidden")]
    [InlineData("...skill", "skill")]
    public void Removes_Leading_Dots(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("skill.", "skill")]
    [InlineData("skill..", "skill")]
    public void Removes_Trailing_Dots(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("-skill", "skill")]
    [InlineData("--skill", "skill")]
    public void Removes_Leading_Hyphens(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("skill-", "skill")]
    [InlineData("skill--", "skill")]
    public void Removes_Trailing_Hyphens(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData(".-.-skill", "skill")]
    [InlineData("-.-.skill", "skill")]
    public void Removes_Mixed_Leading_Dots_And_Hyphens(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Fact]
    public void Returns_UnnamedSkill_For_Empty_String()
    {
        // Act & Assert
        Assert.Equal("unnamed-skill", NameSanitizer.SanitizeName(""));
    }

    [Theory]
    [InlineData("...")]
    [InlineData("---")]
    [InlineData("@#$%")]
    public void Returns_UnnamedSkill_When_Only_Special_Chars(string input)
    {
        // Act & Assert
        Assert.Equal("unnamed-skill", NameSanitizer.SanitizeName(input));
    }

    [Fact]
    public void Handles_Very_Long_Names()
    {
        // Arrange
        var longName = new string('a', 300);

        // Act
        var result = NameSanitizer.SanitizeName(longName);

        // Assert
        Assert.Equal(255, result.Length);
        Assert.Equal(new string('a', 255), result);
    }

    [Theory]
    [InlineData("skill日本語", "skill")]
    [InlineData("émoji🎉skill", "moji-skill")]
    public void Handles_Unicode_Characters(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Theory]
    [InlineData("vercel/next.js", "vercel-next.js")]
    [InlineData("owner/repo-name", "owner-repo-name")]
    public void Handles_GitHub_Repo_Style_Names(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }

    [Fact]
    public void Handles_Urls()
    {
        // Act & Assert
        Assert.Equal("https-example.com", NameSanitizer.SanitizeName("https://example.com"));
    }

    [Theory]
    [InlineData("docs.example.com", "docs.example.com")]
    [InlineData("bun.sh", "bun.sh")]
    public void Handles_Mintlify_Style_Names(string input, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, NameSanitizer.SanitizeName(input));
    }
}
