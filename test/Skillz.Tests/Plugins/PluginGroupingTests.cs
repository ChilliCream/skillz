using Skillz.Plugins;
using Xunit;

namespace Skillz.Tests.Plugins;

public class PluginGroupingTests : IDisposable
{
    private readonly string _testDir;
    private readonly PluginGrouping _grouping = new();

    public PluginGroupingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"skillz-grouping-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private void WriteManifest(string relativePath, string contents)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    [Fact]
    public async Task MapsSkillPathsToPluginNames()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                {
                  "name": "document-skills",
                  "source": "./",
                  "skills": ["./skills/xlsx", "./skills/docx"]
                },
                {
                  "name": "example-skills",
                  "source": "./",
                  "skills": ["./skills/art"]
                }
              ]
            }
            """);

        // Act
        var groupings = await _grouping.GetPluginGroupingsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var xlsxPath = Path.GetFullPath(Path.Combine(_testDir, "skills", "xlsx"));
        var docxPath = Path.GetFullPath(Path.Combine(_testDir, "skills", "docx"));
        var artPath = Path.GetFullPath(Path.Combine(_testDir, "skills", "art"));

        Assert.Equal("document-skills", groupings[xlsxPath]);
        Assert.Equal("document-skills", groupings[docxPath]);
        Assert.Equal("example-skills", groupings[artPath]);
    }

    [Fact]
    public async Task HandlesNestedPluginSources()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                {
                  "name": "nested-plugin",
                  "source": "./plugins/my-plugin",
                  "skills": ["./skills/deep"]
                }
              ]
            }
            """);

        // Act
        var groupings = await _grouping.GetPluginGroupingsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var expected = Path.GetFullPath(Path.Combine(_testDir, "plugins", "my-plugin", "skills", "deep"));
        Assert.Equal("nested-plugin", groupings[expected]);
    }

    [Fact]
    public async Task MapsSkillsFromPluginJson()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "name": "single-plugin",
              "skills": ["./skills/one", "./skills/two"]
            }
            """);

        // Act
        var groupings = await _grouping.GetPluginGroupingsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var onePath = Path.GetFullPath(Path.Combine(_testDir, "skills", "one"));
        var twoPath = Path.GetFullPath(Path.Combine(_testDir, "skills", "two"));

        Assert.Equal("single-plugin", groupings[onePath]);
        Assert.Equal("single-plugin", groupings[twoPath]);
    }

    [Fact]
    public async Task SkipsPluginsWithoutName()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                { "source": "./", "skills": ["./skills/orphan"] }
              ]
            }
            """);

        // Act
        var groupings = await _grouping.GetPluginGroupingsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(groupings);
    }

    [Fact]
    public async Task SkipsTraversalAttempts()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                {
                  "name": "bad-plugin",
                  "source": "./",
                  "skills": ["../../outside"]
                }
              ]
            }
            """);

        // Act
        var groupings = await _grouping.GetPluginGroupingsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(groupings);
    }
}
