using Skillz.Plugins;
using Xunit;

namespace Skillz.Tests.Plugins;

public class PluginManifestTests : IDisposable
{
    private readonly string _testDir;
    private readonly PluginManifest _manifest = new();

    public PluginManifestTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"skillz-manifest-test-{Guid.NewGuid():N}");
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
    public async Task DiscoversSearchDirs_FromMarketplaceJson()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "plugins": [
                {
                  "name": "test-plugin",
                  "source": "./plugins/test-plugin",
                  "skills": ["./skills/test-skill"]
                }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var expected = Path.Combine(_testDir, "plugins", "test-plugin", "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task RespectsMetadataPluginRoot()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "metadata": { "pluginRoot": "./plugins" },
              "plugins": [
                {
                  "name": "my-plugin",
                  "source": "./my-plugin",
                  "skills": ["./skills/my-skill"]
                }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var expected = Path.Combine(_testDir, "plugins", "my-plugin", "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task DiscoversSearchDirs_FromPluginJson()
    {
        WriteManifest(".claude-plugin/plugin.json", """
            {
              "name": "single-plugin",
              "skills": ["./skills/single-skill"]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var expected = Path.Combine(_testDir, "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task SkipsRemoteSourceObjects()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "plugins": [
                {
                  "name": "remote-plugin",
                  "source": { "source": "github", "repo": "owner/repo" },
                  "skills": ["./skills/remote-skill"]
                }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        Assert.Empty(dirs);
    }

    [Fact]
    public async Task MissingManifest_ReturnsEmpty()
    {
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        Assert.Empty(dirs);
    }

    [Fact]
    public async Task InvalidJson_ReturnsEmpty()
    {
        WriteManifest(".claude-plugin/marketplace.json", "not valid json");

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        Assert.Empty(dirs);
    }

    [Fact]
    public async Task RejectsTraversalViaSource()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "plugins": [
                { "source": "../../../etc", "skills": ["./passwd"] }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        Assert.All(dirs, d => Assert.True(PathContainment.IsContainedIn(d, _testDir)));
    }

    [Fact]
    public async Task RejectsTraversalViaSkillPath()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "plugins": [
                { "source": "./legit", "skills": ["../../../outside/skill"] }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        Assert.All(dirs, d => Assert.True(PathContainment.IsContainedIn(d, _testDir)));
    }

    [Fact]
    public async Task RejectsAbsolutePathsInSkills()
    {
        WriteManifest(".claude-plugin/plugin.json", """
            {
              "skills": ["/etc/passwd", "/tmp/malicious-skill"]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var conventional = Path.Combine(_testDir, "skills");
        Assert.Single(dirs);
        Assert.Equal(Path.GetFullPath(conventional), Path.GetFullPath(dirs[0]));
    }

    [Fact]
    public async Task RejectsSourceWithoutDotSlashPrefix()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "plugins": [
                { "source": "bare-plugin", "skills": ["./skills/skill1"] },
                { "source": "./valid-plugin", "skills": ["./skills/skill2"] }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var bareSkills = Path.Combine(_testDir, "bare-plugin", "skills");
        var validSkills = Path.Combine(_testDir, "valid-plugin", "skills");

        Assert.DoesNotContain(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(bareSkills));
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(validSkills));
    }

    [Fact]
    public async Task RejectsPluginRootWithoutDotSlashPrefix()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "metadata": { "pluginRoot": "custom-plugins" },
              "plugins": [
                { "source": "./my-plugin", "skills": ["./skills/skill"] }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        Assert.Empty(dirs);
    }

    [Fact]
    public async Task RejectsSkillPathWithoutDotSlashPrefix()
    {
        WriteManifest(".claude-plugin/plugin.json", """
            {
              "skills": ["invalid-loc/bare-skill", "./valid-loc/valid-skill"]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var validParent = Path.Combine(_testDir, "valid-loc");
        var invalidParent = Path.Combine(_testDir, "invalid-loc");

        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(validParent));
        Assert.DoesNotContain(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(invalidParent));
    }

    [Fact]
    public async Task RootLevelPlugin_WithoutSource_AddsConventionalDir()
    {
        WriteManifest(".claude-plugin/marketplace.json", """
            {
              "plugins": [
                { "name": "root-plugin", "skills": ["./skills/root-skill"] }
              ]
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var expected = Path.Combine(_testDir, "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task PluginJson_WithoutSkillsField_StillAddsConventionalDir()
    {
        WriteManifest(".claude-plugin/plugin.json", """
            {
              "name": "plugin-without-skills-field"
            }
            """);

        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir);

        var expected = Path.Combine(_testDir, "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }
}
