using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Skills;

public class FrontmatterParserTests
{
    [Fact]
    public void Parses_Valid_Frontmatter_With_Name_And_Description()
    {
        // Arrange
        var raw = "---\nname: my-skill\ndescription: A test skill\n---\nSkill body content here.";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Equal("my-skill", result.Data["name"]);
        Assert.Equal("A test skill", result.Data["description"]);
        Assert.Equal("Skill body content here.", result.Content);
    }

    [Fact]
    public void Returns_Empty_Data_For_Content_Without_Frontmatter()
    {
        // Arrange
        var raw = "Just plain content with no frontmatter.";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Empty(result.Data);
        Assert.Equal(raw, result.Content);
    }

    [Fact]
    public void Handles_Crlf_Line_Endings()
    {
        // Arrange
        var raw = "---\r\nname: my-skill\r\ndescription: A test\r\n---\r\nBody";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Equal("my-skill", result.Data["name"]);
        Assert.Equal("A test", result.Data["description"]);
        Assert.Equal("Body", result.Content);
    }

    [Fact]
    public void Handles_Empty_Yaml_Block()
    {
        // Arrange
        var raw = "---\n\n---\nContent";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Empty(result.Data);
        Assert.Equal("Content", result.Content);
    }

    [Fact]
    public void Handles_Content_After_Frontmatter()
    {
        // Arrange
        var raw = "---\nname: test\n---\nLine 1\nLine 2\nLine 3";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Equal("test", result.Data["name"]);
        Assert.Equal("Line 1\nLine 2\nLine 3", result.Content);
    }

    [Fact]
    public void Handles_No_Trailing_Newline_After_Closing_Delimiter()
    {
        // Arrange
        var raw = "---\nname: test\n---";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Equal("test", result.Data["name"]);
        Assert.Equal("", result.Content);
    }

    [Fact]
    public void Handles_Single_Newline_After_Closing_Delimiter()
    {
        // Arrange
        var raw = "---\nname: test\n---\n";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Equal("test", result.Data["name"]);
        Assert.Equal("", result.Content);
    }
}
