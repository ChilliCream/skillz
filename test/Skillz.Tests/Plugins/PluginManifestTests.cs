using Skillz.Paths;
using Skillz.Plugins;
using Skillz.Utils;
using Xunit;

namespace Skillz.Tests.Plugins;

public class PluginManifestTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _outsideDir;
    private readonly PluginManifest _manifest = new(new SystemFileStore());

    public PluginManifestTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"skillz-manifest-test-{Guid.NewGuid():N}");
        _outsideDir = Path.Combine(Path.GetTempPath(), $"skillz-manifest-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_outsideDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }

        if (Directory.Exists(_outsideDir))
        {
            Directory.Delete(_outsideDir, recursive: true);
        }
    }

    private static bool TryCreateDirectorySymlink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateFileSymlink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
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

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var expected = Path.Combine(_testDir, "plugins", "test-plugin", "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_NotReadManifest_When_PluginJsonLeafIsASymlink()
    {
        // A plugin.json whose leaf is a symlink to an out-of-tree file must be refused, not
        // followed, so the out-of-tree bytes are never read and deserialized into a manifest.
        var outsideManifest = Path.Combine(_outsideDir, "plugin.json");
        File.WriteAllText(outsideManifest, """{ "skills": ["./leaked/SKILL.md"] }""");

        // The directory the planted manifest points at is itself in-tree, so the only thing
        // that can keep it out of the result is the no-follow refusal of the symlinked leaf.
        Directory.CreateDirectory(Path.Combine(_testDir, "leaked"));
        Directory.CreateDirectory(Path.Combine(_testDir, ".claude-plugin"));
        if (!TryCreateFileSymlink(Path.Combine(_testDir, ".claude-plugin", "plugin.json"), outsideManifest))
        {
            return; // platform refused symlink creation without privilege
        }

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var leaked = Path.Combine(_testDir, "leaked");
        Assert.DoesNotContain(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(leaked));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_ResolveSourceUnderPluginRoot_When_MetadataSpecifiesPluginRoot()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
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

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var expected = Path.Combine(_testDir, "plugins", "my-plugin", "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task DiscoversSearchDirs_FromPluginJson()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "name": "single-plugin",
              "skills": ["./skills/single-skill"]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var expected = Path.Combine(_testDir, "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_ReturnEmpty_When_SourceIsRemoteObject()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
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

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(dirs);
    }

    [Fact]
    public async Task MissingManifest_ReturnsEmpty()
    {
        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(dirs);
    }

    [Fact]
    public async Task InvalidJson_ReturnsEmpty()
    {
        // Arrange
        WriteManifest(".claude-plugin/marketplace.json", "not valid json");

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(dirs);
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_NotAddDefaultSkillsDir_When_It_Is_A_Symlink_Escaping_Base()
    {
        // Arrange: a single-plugin manifest with no explicit skills, whose default skills
        // directory is a symlink pointing OUTSIDE the base. Discovery would descend into it, so
        // it must be rejected by the containment re-check.
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "name": "single-plugin"
            }
            """);

        if (!TryCreateDirectorySymlink(Path.Combine(_testDir, "skills"), _outsideDir))
        {
            return; // platform refused symlink creation without privilege
        }

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert: the escaping default skills directory is not added.
        var escaping = Path.Combine(_testDir, "skills");
        Assert.DoesNotContain(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(escaping));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_ContainAllResultsWithinBase_When_SourceAttemptsTraversal()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                { "source": "../../../etc", "skills": ["./passwd"] }
              ]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(dirs, d => Assert.True(SafePath.Contains(_testDir, d, LeafPolicy.Follow)));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_ContainAllResultsWithinBase_When_SkillPathAttemptsTraversal()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                { "source": "./legit", "skills": ["../../../outside/skill"] }
              ]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(dirs, d => Assert.True(SafePath.Contains(_testDir, d, LeafPolicy.Follow)));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_IgnoreAbsolutePathsAndFallBackToConventionalDir_When_SkillsAreAbsolute()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "skills": ["/etc/passwd", "/tmp/malicious-skill"]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var conventional = Path.Combine(_testDir, "skills");
        Assert.Single(dirs);
        Assert.Equal(Path.GetFullPath(conventional), Path.GetFullPath(dirs[0]));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_IgnoreSource_When_SourceLacksDotSlashPrefix()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                { "source": "bare-plugin", "skills": ["./skills/skill1"] },
                { "source": "./valid-plugin", "skills": ["./skills/skill2"] }
              ]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var bareSkills = Path.Combine(_testDir, "bare-plugin", "skills");
        var validSkills = Path.Combine(_testDir, "valid-plugin", "skills");

        Assert.DoesNotContain(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(bareSkills));
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(validSkills));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_ReturnEmpty_When_PluginRootLacksDotSlashPrefix()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "metadata": { "pluginRoot": "custom-plugins" },
              "plugins": [
                { "source": "./my-plugin", "skills": ["./skills/skill"] }
              ]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(dirs);
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_IgnoreSkillPath_When_SkillPathLacksDotSlashPrefix()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "skills": ["invalid-loc/bare-skill", "./valid-loc/valid-skill"]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var validParent = Path.Combine(_testDir, "valid-loc");
        var invalidParent = Path.Combine(_testDir, "invalid-loc");

        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(validParent));
        Assert.DoesNotContain(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(invalidParent));
    }

    [Fact]
    public async Task RootLevelPlugin_WithoutSource_AddsConventionalDir()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                { "name": "root-plugin", "skills": ["./skills/root-skill"] }
              ]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var expected = Path.Combine(_testDir, "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task PluginJson_WithoutSkillsField_StillAddsConventionalDir()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "name": "plugin-without-skills-field"
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var expected = Path.Combine(_testDir, "skills");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(expected));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_IgnoreSkillPath_When_SkillPathContainsControlByte()
    {
        // Arrange
        // A NUL in a skills[] entry would otherwise reach Path.Combine and crash discovery; the
        // poisoned entry must be dropped while a clean sibling under the same plugin survives.
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "skills": ["./skills/\u0000bad-skill", "./valid-loc/valid-skill"]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        var validParent = Path.Combine(_testDir, "valid-loc");
        Assert.Contains(dirs, d => Path.GetFullPath(d) == Path.GetFullPath(validParent));
        Assert.DoesNotContain(dirs, d => d.Contains("bad-skill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPluginSkillPaths_Should_ReturnEmpty_When_PluginRootContainsControlByte()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "metadata": { "pluginRoot": "./plug\u001bins" },
              "plugins": [
                { "source": "./my-plugin", "skills": ["./skills/skill"] }
              ]
            }
            """);

        // Act
        var dirs = await _manifest.GetPluginSkillPathsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(dirs);
    }

    [Fact]
    public async Task ReadPlugins_Should_StripTerminalEscapesFromName_When_MarketplaceNameContainsEscapes()
    {
        // Arrange
        // Terminal escape sequences embedded around the benign visible text.
        WriteManifest(
            ".claude-plugin/marketplace.json",
            """
            {
              "plugins": [
                {
                  "name": "\u001b]0;title\u0007Real\u001b[2J Name",
                  "source": "./",
                  "skills": ["./skills/skill"]
                }
              ]
            }
            """);

        // Act
        var plugins = await PluginManifest.ReadPluginsAsync(
            new SystemFileStore(),
            _testDir,
            TestContext.Current.CancellationToken);

        // Assert
        var plugin = Assert.Single(plugins);
        Assert.DoesNotContain('\u001b', plugin.Name!);
        Assert.Equal("Real Name", plugin.Name);
    }

    [Fact]
    public async Task ReadPlugins_Should_RejectMarketplaceEntry_When_PluginBaseResolvesOutsideBaseViaSymlink()
    {
        // Arrange
        // 'linkdir' under the base is a symlink to a directory outside the base, so the
        // constructed pluginBase ('linkdir/plugin') is lexically contained but resolves
        // outside the base once the symlinked parent is followed.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outsideDir = Path.Combine(Path.GetTempPath(), $"skillz-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        try
        {
            var linkPath = Path.Combine(_testDir, "linkdir");
            Directory.CreateSymbolicLink(linkPath, outsideDir);

            WriteManifest(
                ".claude-plugin/marketplace.json",
                """
                {
                  "plugins": [
                    { "name": "escaping-plugin", "source": "./linkdir/plugin", "skills": ["./skills/skill"] }
                  ]
                }
                """);

            // Act
            var plugins = await PluginManifest.ReadPluginsAsync(
                new SystemFileStore(),
                _testDir,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Empty(plugins);
        }
        finally
        {
            Directory.Delete(outsideDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReadPlugins_Should_StripTerminalEscapesFromName_When_PluginJsonNameContainsEscapes()
    {
        // Arrange
        WriteManifest(
            ".claude-plugin/plugin.json",
            """
            {
              "name": "\u001b]0;title\u0007Real\u001b[2J Name",
              "skills": ["./skills/skill"]
            }
            """);

        // Act
        var plugins = await PluginManifest.ReadPluginsAsync(
            new SystemFileStore(),
            _testDir,
            TestContext.Current.CancellationToken);

        // Assert
        var plugin = Assert.Single(plugins);
        Assert.DoesNotContain('\u001b', plugin.Name!);
        Assert.Equal("Real Name", plugin.Name);
    }
}
