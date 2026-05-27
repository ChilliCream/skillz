using System.Text.Json;
using Skillz.Lock;
using Xunit;

namespace Skillz.Tests.Lock;

public class ProjectLockFileTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectLockFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "skillz-locktest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void GetLockPath_Returns_SkillsLockJson_In_Given_Directory()
    {
        var lockFile = new ProjectLockFile();

        var path = lockFile.GetLockPath("/some/project");

        Assert.Equal(Path.Combine("/some/project", "skills-lock.json"), path);
    }

    [Fact]
    public void GetLockPath_Uses_Cwd_When_No_Directory_Given()
    {
        var lockFile = new ProjectLockFile();

        var path = lockFile.GetLockPath();

        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "skills-lock.json"), path);
    }

    [Fact]
    public async Task ReadAsync_Returns_Empty_Lock_When_File_Does_Not_Exist()
    {
        var lockFile = new ProjectLockFile();

        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);

        Assert.Equal(ProjectLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task ReadAsync_Reads_A_Valid_Lock_File()
    {
        var content = """
            {
              "version": 1,
              "skills": {
                "my-skill": {
                  "source": "vercel-labs/skills",
                  "sourceType": "github",
                  "computedHash": "abc123"
                }
              }
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "skills-lock.json"),
            content,
            TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Version);
        Assert.Single(result.Skills);
        var entry = result.Skills["my-skill"];
        Assert.Equal("vercel-labs/skills", entry.Source);
        Assert.Equal("github", entry.SourceType);
        Assert.Equal("abc123", entry.ComputedHash);
    }

    [Fact]
    public async Task ReadAsync_Returns_Empty_For_Corrupted_Json()
    {
        var conflicted = """
            {
              "version": 1,
              "skills": {
            <<<<<<< HEAD
                "skill-a": { "source": "org/repo-a", "sourceType": "github", "computedHash": "aaa" }
            =======
                "skill-b": { "source": "org/repo-b", "sourceType": "github", "computedHash": "bbb" }
            >>>>>>> feature-branch
              }
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "skills-lock.json"),
            conflicted,
            TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);

        Assert.Equal(ProjectLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task ReadAsync_Returns_Empty_For_Old_Version()
    {
        var content = """
            {
              "version": 0,
              "skills": {
                "my-skill": {
                  "source": "org/repo",
                  "sourceType": "github",
                  "computedHash": "abc"
                }
              }
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "skills-lock.json"),
            content,
            TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);

        Assert.Equal(ProjectLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task WriteAsync_Writes_Sorted_Keys_With_Trailing_Newline()
    {
        var lockFile = new ProjectLockFile();
        var file = new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
            {
                ["zebra-skill"] = new() { Source = "org/z", SourceType = "github", ComputedHash = "zzz" },
                ["alpha-skill"] = new() { Source = "org/a", SourceType = "github", ComputedHash = "aaa" },
                ["middle-skill"] = new() { Source = "org/m", SourceType = "github", ComputedHash = "mmm" },
            },
        };

        await lockFile.WriteAsync(file, _tempDir, TestContext.Current.CancellationToken);

        var raw = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, "skills-lock.json"),
            TestContext.Current.CancellationToken);
        Assert.EndsWith("\n", raw);

        using var doc = JsonDocument.Parse(raw);
        var skills = doc.RootElement.GetProperty("skills");
        var keys = skills.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "alpha-skill", "middle-skill", "zebra-skill" }, keys);
    }

    [Fact]
    public async Task ReadWrite_Round_Trip_Preserves_Entries()
    {
        var lockFile = new ProjectLockFile();
        var input = new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
            {
                ["round-trip"] = new()
                {
                    Source = "org/repo",
                    SourceType = "github",
                    Ref = "main",
                    SkillPath = "skills/round-trip",
                    ComputedHash = "deadbeef",
                },
            },
        };

        await lockFile.WriteAsync(input, _tempDir, TestContext.Current.CancellationToken);
        var output = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);

        Assert.Equal(1, output.Version);
        var entry = output.Skills["round-trip"];
        Assert.Equal("org/repo", entry.Source);
        Assert.Equal("github", entry.SourceType);
        Assert.Equal("main", entry.Ref);
        Assert.Equal("skills/round-trip", entry.SkillPath);
        Assert.Equal("deadbeef", entry.ComputedHash);
    }

    [Fact]
    public async Task AddEntryAsync_Adds_New_Skill_To_Empty_Lock()
    {
        var lockFile = new ProjectLockFile();

        await lockFile.AddEntryAsync(
            "new-skill",
            new LocalSkillLockEntry { Source = "org/repo", SourceType = "github", ComputedHash = "hash123" },
            _tempDir,
            TestContext.Current.CancellationToken);

        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);
        Assert.Single(result.Skills);
        Assert.Equal("hash123", result.Skills["new-skill"].ComputedHash);
    }

    [Fact]
    public async Task AddEntryAsync_Updates_Existing_Skill_Hash()
    {
        var lockFile = new ProjectLockFile();

        await lockFile.AddEntryAsync(
            "my-skill",
            new LocalSkillLockEntry { Source = "org/repo", SourceType = "github", ComputedHash = "old-hash" },
            _tempDir,
            TestContext.Current.CancellationToken);
        await lockFile.AddEntryAsync(
            "my-skill",
            new LocalSkillLockEntry { Source = "org/repo", SourceType = "github", ComputedHash = "new-hash" },
            _tempDir,
            TestContext.Current.CancellationToken);

        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);
        Assert.Equal("new-hash", result.Skills["my-skill"].ComputedHash);
    }

    [Fact]
    public async Task AddEntryAsync_Preserves_Other_Skills()
    {
        var lockFile = new ProjectLockFile();

        await lockFile.AddEntryAsync(
            "skill-a",
            new LocalSkillLockEntry { Source = "org/a", SourceType = "github", ComputedHash = "aaa" },
            _tempDir,
            TestContext.Current.CancellationToken);
        await lockFile.AddEntryAsync(
            "skill-b",
            new LocalSkillLockEntry { Source = "org/b", SourceType = "github", ComputedHash = "bbb" },
            _tempDir,
            TestContext.Current.CancellationToken);

        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Skills.Count);
        Assert.Equal("aaa", result.Skills["skill-a"].ComputedHash);
        Assert.Equal("bbb", result.Skills["skill-b"].ComputedHash);
    }

    [Fact]
    public async Task RemoveEntryAsync_Removes_Existing_Skill()
    {
        var lockFile = new ProjectLockFile();
        await lockFile.AddEntryAsync(
            "my-skill",
            new LocalSkillLockEntry { Source = "org/repo", SourceType = "github", ComputedHash = "h" },
            _tempDir,
            TestContext.Current.CancellationToken);

        var removed = await lockFile.RemoveEntryAsync(
            "my-skill",
            _tempDir,
            TestContext.Current.CancellationToken);

        Assert.True(removed);
        var result = await lockFile.ReadAsync(_tempDir, TestContext.Current.CancellationToken);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task RemoveEntryAsync_Returns_False_For_Non_Existent_Skill()
    {
        var lockFile = new ProjectLockFile();

        var removed = await lockFile.RemoveEntryAsync(
            "no-such-skill",
            _tempDir,
            TestContext.Current.CancellationToken);

        Assert.False(removed);
    }

    [Fact]
    public async Task HasSkillAsync_Returns_True_When_Skill_Exists()
    {
        var lockFile = new ProjectLockFile();
        await lockFile.AddEntryAsync(
            "found-skill",
            new LocalSkillLockEntry { Source = "org/repo", SourceType = "github", ComputedHash = "h" },
            _tempDir,
            TestContext.Current.CancellationToken);

        var hasFound = await lockFile.HasSkillAsync(
            "found-skill",
            _tempDir,
            TestContext.Current.CancellationToken);
        var hasMissing = await lockFile.HasSkillAsync(
            "missing-skill",
            _tempDir,
            TestContext.Current.CancellationToken);

        Assert.True(hasFound);
        Assert.False(hasMissing);
    }

    [Fact]
    public async Task ComputeSkillFolderHashAsync_Is_Deterministic_And_Sha256()
    {
        var skillDir = Path.Combine(_tempDir, "my-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "SKILL.md"),
            "---\nname: test\ndescription: test\n---\n# Test\n",
            TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var hash1 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);
        var hash2 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        Assert.Equal(hash1, hash2);
        Assert.Matches("^[a-f0-9]{64}$", hash1);
    }

    [Fact]
    public async Task ComputeSkillFolderHashAsync_Changes_When_File_Content_Changes()
    {
        var skillDir = Path.Combine(_tempDir, "my-skill");
        Directory.CreateDirectory(skillDir);
        var skillFile = Path.Combine(skillDir, "SKILL.md");
        await File.WriteAllTextAsync(skillFile, "version 1", TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var hash1 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(skillFile, "version 2", TestContext.Current.CancellationToken);
        var hash2 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ComputeSkillFolderHashAsync_Includes_Nested_Files()
    {
        var skillDir = Path.Combine(_tempDir, "my-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "sub"));
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "SKILL.md"),
            "root",
            TestContext.Current.CancellationToken);
        var nestedFile = Path.Combine(skillDir, "sub", "helper.md");
        await File.WriteAllTextAsync(nestedFile, "nested", TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var hash1 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(nestedFile, "changed", TestContext.Current.CancellationToken);
        var hash2 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ComputeSkillFolderHashAsync_Ignores_Git_And_NodeModules()
    {
        var skillDir = Path.Combine(_tempDir, "my-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "SKILL.md"),
            "content",
            TestContext.Current.CancellationToken);

        var lockFile = new ProjectLockFile();
        var hash1 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        Directory.CreateDirectory(Path.Combine(skillDir, ".git"));
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, ".git", "HEAD"),
            "ref: refs/heads/main",
            TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(skillDir, "node_modules", "foo"));
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "node_modules", "foo", "index.js"),
            "noop",
            TestContext.Current.CancellationToken);

        var hash2 = await lockFile.ComputeSkillFolderHashAsync(skillDir, TestContext.Current.CancellationToken);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task Lock_Output_Has_No_Timestamps_For_Merge_Friendliness()
    {
        var lockFile = new ProjectLockFile();
        await lockFile.AddEntryAsync(
            "skill-a",
            new LocalSkillLockEntry { Source = "org/a", SourceType = "github", ComputedHash = "aaa" },
            _tempDir,
            TestContext.Current.CancellationToken);

        var raw = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, "skills-lock.json"),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("installedAt", raw);
        Assert.DoesNotContain("updatedAt", raw);
    }
}
