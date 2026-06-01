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

    [Fact]
    public void Parses_Nested_Mapping_As_Dictionary_Of_Object_With_Scalars_As_Strings()
    {
        // Arrange
        var raw = "---\nname: my-skill\nmetadata:\n  type: tool\n  internal: true\n---\nBody";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert — the exact shape SkillDiscovery pattern-matches; boolean stays the
        // string "true" because the internal-skill gate compares against "true".
        var metadata = Assert.IsType<Dictionary<object, object>>(result.Data["metadata"]);
        Assert.Equal("tool", metadata["type"]);
        Assert.Equal("true", metadata["internal"]);
    }

    [Fact]
    public void Parses_Sequence_As_List_Of_Object()
    {
        // Arrange
        var raw = "---\ntags:\n  - alpha\n  - beta\n---\nBody";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        var tags = Assert.IsType<List<object>>(result.Data["tags"]);
        Assert.Equal(new object[] { "alpha", "beta" }, tags);
    }

    [Fact]
    public void Returns_Empty_Data_When_Frontmatter_Is_Not_A_Mapping()
    {
        // Arrange — a sequence where a mapping is expected
        var raw = "---\n- just\n- a\n- list\n---\nBody";

        // Act
        var result = FrontmatterParser.Parse(raw);

        // Assert
        Assert.Empty(result.Data);
        Assert.Equal("Body", result.Content);
    }
}
