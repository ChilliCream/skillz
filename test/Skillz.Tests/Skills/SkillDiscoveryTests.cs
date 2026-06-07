using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Tests.TestServices;
using Skillz.Utils;
using Xunit;

namespace Skillz.Tests.Skills;

public class SkillDiscoveryTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _outsideDir;
    private readonly SkillDiscovery _discovery;

    public SkillDiscoveryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"skillz-discovery-test-{Guid.NewGuid():N}");
        _outsideDir = Path.Combine(Path.GetTempPath(), $"skillz-discovery-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_outsideDir);
        var fileStore = new SystemFileStore();
        _discovery = new SkillDiscovery(
            new PluginManifest(fileStore),
            new PluginGrouping(fileStore),
            fileStore,
            new FakeSystemEnvironment());
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

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void WriteSkill(string relativeDir, string name, string description)
    {
        var dir = Path.Combine(_testDir, relativeDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n---\n\n# {name}\n");
    }

    private static string SkillMd(string name, string description)
        => $"---\nname: {name}\ndescription: {description}\n---\n\n# {name}\n";

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

    [Fact]
    public async Task FullDepth_False_Returns_Only_Root_Skill()
    {
        // Arrange
        WriteSkill(".", "root-skill", "Root level skill");
        WriteSkill("skills/nested-skill", "nested-skill", "Nested skill");

        // Act
        var skills = await _discovery.DiscoverAsync(
            _testDir,
            subpath: null,
            options: new SkillDiscoveryOptions(FullDepth: false),
            cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal("root-skill", skills[0].Name);
    }

    [Fact]
    public async Task FullDepth_True_Returns_All_Skills()
    {
        // Arrange
        WriteSkill(".", "root-skill", "Root level skill");
        WriteSkill("skills/nested-skill-1", "nested-skill-1", "Nested skill 1");
        WriteSkill("skills/nested-skill-2", "nested-skill-2", "Nested skill 2");

        // Act
        var skills = await _discovery.DiscoverAsync(
            _testDir,
            subpath: null,
            options: new SkillDiscoveryOptions(FullDepth: true),
            cancellationToken: Token);

        // Assert
        Assert.Equal(3, skills.Length);
        var names = skills.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "nested-skill-1", "nested-skill-2", "root-skill" }, names);
    }

    [Fact]
    public async Task Default_Options_Returns_Only_Root_When_Root_Skill_Exists()
    {
        // Arrange
        WriteSkill(".", "root-skill", "Root level skill");
        WriteSkill("skills/nested-skill", "nested-skill", "Nested skill");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal("root-skill", skills[0].Name);
    }

    [Fact]
    public async Task Finds_All_Nested_Skills_When_No_Root_Skill()
    {
        // Arrange
        WriteSkill("skills/skill-1", "skill-1", "Skill 1");
        WriteSkill("skills/skill-2", "skill-2", "Skill 2");

        // Act
        var defaultSkills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Equal(2, defaultSkills.Length);

        // Act
        var fullDepthSkills = await _discovery.DiscoverAsync(
            _testDir,
            subpath: null,
            options: new SkillDiscoveryOptions(FullDepth: true),
            cancellationToken: Token);

        // Assert
        Assert.Equal(2, fullDepthSkills.Length);
    }

    [Fact]
    public async Task Deduplicates_Root_And_Nested_With_Same_Name()
    {
        // Arrange
        WriteSkill(".", "my-skill", "Root level skill");
        WriteSkill("skills/my-skill", "my-skill", "Nested skill with same name");

        // Act
        var skills = await _discovery.DiscoverAsync(
            _testDir,
            subpath: null,
            options: new SkillDiscoveryOptions(FullDepth: true),
            cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal("my-skill", skills[0].Name);
    }

    [Fact]
    public async Task Rejects_Subpath_Escaping_BasePath()
    {
        // Act & Assert
        await Assert.ThrowsAsync<CliException>(async () =>
            await _discovery.DiscoverAsync(_testDir, subpath: "../escape", options: null, cancellationToken: Token)
        );
    }

    [Fact]
    public async Task Honors_Subpath_When_Provided()
    {
        // Arrange
        WriteSkill("nested/skills/sub-skill", "sub-skill", "Skill under subpath");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: "nested", options: null, cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal("sub-skill", skills[0].Name);
    }

    [Fact]
    public async Task Skips_SkipDirs_In_Recursive_Search()
    {
        // Arrange
        WriteSkill("node_modules/should-not-find", "hidden-skill", "Should be skipped");
        WriteSkill("regular-dir/visible-skill", "visible-skill", "Should be found");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal("visible-skill", skills[0].Name);
    }

    [Fact]
    public async Task Does_Not_Discover_Skill_Reached_Only_Through_OutOfTree_Directory_Symlink()
    {
        // Arrange: a valid skill lives OUTSIDE the repo tree; an in-tree directory symlink points
        // at it. A malicious clone could plant exactly this to read e.g. ~/.ssh via discovery.
        Directory.CreateDirectory(Path.Combine(_outsideDir, "secret-skill"));
        File.WriteAllText(
            Path.Combine(_outsideDir, "secret-skill", "SKILL.md"),
            SkillMd("secret-skill", "Lives outside the repository tree"));

        Directory.CreateDirectory(Path.Combine(_testDir, "skills"));
        if (!TryCreateDirectorySymlink(Path.Combine(_testDir, "skills", "escape"), _outsideDir))
        {
            return; // platform refused symlink creation without privilege
        }

        // Act
        var skills = await _discovery.DiscoverAsync(
            _testDir,
            subpath: null,
            options: new SkillDiscoveryOptions(FullDepth: true),
            cancellationToken: Token);

        // Assert: the out-of-tree skill behind the symlink is never discovered.
        Assert.DoesNotContain(skills, s => s.Name == "secret-skill");
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Does_Not_Leak_OutOfTree_File_When_SkillMd_Is_A_Symlink_To_It()
    {
        // Arrange: a discovered, in-tree skill directory whose SKILL.md leaf is a symlink to an
        // out-of-tree file. The symlinked leaf must be refused, not dereferenced into RawContent.
        var secret = Path.Combine(_outsideDir, "secret.md");
        File.WriteAllText(secret, SkillMd("leaked-secret", "This frontmatter must not be discovered"));

        var skillDir = Path.Combine(_testDir, "skills", "leaky");
        Directory.CreateDirectory(skillDir);
        if (!TryCreateFileSymlink(Path.Combine(skillDir, "SKILL.md"), secret))
        {
            return; // platform refused symlink creation without privilege
        }

        // Act
        var skills = await _discovery.DiscoverAsync(
            _testDir,
            subpath: null,
            options: new SkillDiscoveryOptions(FullDepth: true),
            cancellationToken: Token);

        // Assert: the out-of-tree file's frontmatter never becomes a discovered skill.
        Assert.DoesNotContain(skills, s => s.Name == "leaked-secret");
        Assert.DoesNotContain(skills, s => s.RawContent is not null && s.RawContent.Contains("leaked-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skips_Skill_With_Missing_Name()
    {
        // Arrange
        var dir = Path.Combine(_testDir, "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\ndescription: missing name\n---\nBody\n");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Skips_Skill_With_Missing_Description()
    {
        // Arrange
        var dir = Path.Combine(_testDir, "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: only-name\n---\nBody\n");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Skips_Skill_With_Control_Byte_In_Directory_Name()
    {
        // Windows refuses to create a directory whose name contains a control byte, so the
        // scenario this guards against cannot occur there - the filesystem rejects it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange: a skill directory whose name carries an ESC byte must be skipped, not returned -
        // Skill.Path stays byte-exact for file I/O, so a poisoned path is rejected rather than rewritten.
        var dir = Path.Combine(_testDir, "skills", "bad\u001bdir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            "---\nname: poisoned\ndescription: Has a control byte in its directory name\n---\nBody\n");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Skips_Skill_With_Name_That_Is_Only_Escape_Bytes()
    {
        // Arrange: a name of pure escape bytes is non-empty in the frontmatter but collapses to ""
        // once sanitized, so it must be rejected against the sanitized value.
        var dir = Path.Combine(_testDir, "skills", "ghost");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            "---\nname: \"\\u001b[31m\\u001b[0m\"\ndescription: Name collapses to empty after sanitization\n---\nBody\n");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Discovers_Skill_With_Unicode_Name()
    {
        // Arrange: a pure-Unicode name (CJK) survives sanitization unchanged and must still be found.
        WriteSkill("skills/unicode-skill", "中文", "A skill with a Unicode name");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal("中文", skills[0].Name);
    }

    [Fact]
    public async Task Discovers_From_Agent_Specific_Priority_Dirs()
    {
        // Arrange
        WriteSkill(".claude/skills/claude-skill", "claude-skill", "Claude-specific skill");
        WriteSkill(".roo/skills/roo-skill", "roo-skill", "Roo-specific skill");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Equal(2, skills.Length);
        var names = skills.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "claude-skill", "roo-skill" }, names);
    }

    [Fact]
    public async Task Returns_Empty_When_BasePath_Has_No_Skills()
    {
        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Sets_Path_To_Skill_Directory()
    {
        // Arrange
        WriteSkill("skills/my-skill", "my-skill", "A skill");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.Equal(Path.GetFullPath(Path.Combine(_testDir, "skills", "my-skill")), Path.GetFullPath(skills[0].Path));
    }

    [Fact]
    public async Task Includes_RawContent()
    {
        // Arrange
        WriteSkill("skills/my-skill", "my-skill", "Has content");

        // Act
        var skills = await _discovery.DiscoverAsync(_testDir, subpath: null, options: null, cancellationToken: Token);

        // Assert
        Assert.Single(skills);
        Assert.NotNull(skills[0].RawContent);
        Assert.Contains("name: my-skill", skills[0].RawContent!, StringComparison.Ordinal);
    }
}
