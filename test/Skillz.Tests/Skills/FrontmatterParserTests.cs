using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Skills;

public class FrontmatterParserTests
{
    [Fact]
    public void Parses_Valid_Frontmatter_With_Name_And_Description()
    {
        var raw = "---\nname: my-skill\ndescription: A test skill\n---\nSkill body content here.";
        var result = FrontmatterParser.Parse(raw);

        Assert.Equal("my-skill", result.Data["name"]);
        Assert.Equal("A test skill", result.Data["description"]);
        Assert.Equal("Skill body content here.", result.Content);
    }

    [Fact]
    public void Returns_Empty_Data_For_Content_Without_Frontmatter()
    {
        var raw = "Just plain content with no frontmatter.";
        var result = FrontmatterParser.Parse(raw);

        Assert.Empty(result.Data);
        Assert.Equal(raw, result.Content);
    }

    [Fact]
    public void Handles_Crlf_Line_Endings()
    {
        var raw = "---\r\nname: my-skill\r\ndescription: A test\r\n---\r\nBody";
        var result = FrontmatterParser.Parse(raw);

        Assert.Equal("my-skill", result.Data["name"]);
        Assert.Equal("A test", result.Data["description"]);
        Assert.Equal("Body", result.Content);
    }

    [Fact]
    public void Handles_Empty_Yaml_Block()
    {
        var raw = "---\n\n---\nContent";
        var result = FrontmatterParser.Parse(raw);

        Assert.Empty(result.Data);
        Assert.Equal("Content", result.Content);
    }

    [Fact]
    public void Handles_Content_After_Frontmatter()
    {
        var raw = "---\nname: test\n---\nLine 1\nLine 2\nLine 3";
        var result = FrontmatterParser.Parse(raw);

        Assert.Equal("test", result.Data["name"]);
        Assert.Equal("Line 1\nLine 2\nLine 3", result.Content);
    }

    [Fact]
    public void Handles_No_Trailing_Newline_After_Closing_Delimiter()
    {
        var raw = "---\nname: test\n---";
        var result = FrontmatterParser.Parse(raw);

        Assert.Equal("test", result.Data["name"]);
        Assert.Equal("", result.Content);
    }

    [Fact]
    public void Handles_Single_Newline_After_Closing_Delimiter()
    {
        var raw = "---\nname: test\n---\n";
        var result = FrontmatterParser.Parse(raw);

        Assert.Equal("test", result.Data["name"]);
        Assert.Equal("", result.Content);
    }
}
