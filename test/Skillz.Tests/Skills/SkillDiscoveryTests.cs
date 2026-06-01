using Skillz.Plugins;
using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Skills;

public class SkillDiscoveryTests : IDisposable
{
    private readonly string _testDir;
    private readonly SkillDiscovery _discovery;

    public SkillDiscoveryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"skillz-discovery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _discovery = new SkillDiscovery(new PluginManifest(), new PluginGrouping());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
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
