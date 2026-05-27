using Skillz.Install;
using Skillz.Lock;
using Xunit;

namespace Skillz.Tests.Lock;

public class GlobalLockFileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly XdgPaths _xdgPaths;

    public GlobalLockFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "skillz-globallock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var stateDir = Path.Combine(_tempDir, "state");
        Directory.CreateDirectory(stateDir);
        _xdgPaths = new XdgPaths(_tempDir, name => name == "XDG_STATE_HOME" ? stateDir : null);
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
        catch { }
    }

    private static DateTime FixedNow => new(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_Returns_Empty_When_File_Does_Not_Exist()
    {
        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);

        var result = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GlobalLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task AddEntryAsync_Stores_Skill_With_InstalledAt_And_UpdatedAt()
    {
        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);

        await lockFile.AddEntryAsync(
            "my-skill",
            new SkillLockEntry
            {
                Source = "org/repo",
                SourceType = "github",
                SourceUrl = "https://github.com/org/repo.git",
                SkillFolderHash = "abc"
            },
            TestContext.Current.CancellationToken);

        var entry = await lockFile.GetEntryAsync("my-skill", TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.Equal("org/repo", entry!.Source);
        Assert.Equal(FixedNow.ToString("o"), entry.InstalledAt);
        Assert.Equal(FixedNow.ToString("o"), entry.UpdatedAt);
    }

    [Fact]
    public async Task AddEntryAsync_Preserves_InstalledAt_On_Update()
    {
        var firstTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondTime = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var current = firstTime;

        var lockFile = new GlobalLockFile(_xdgPaths, () => current);

        await lockFile.AddEntryAsync(
            "my-skill",
            new SkillLockEntry
            {
                Source = "org/repo",
                SourceType = "github",
                SourceUrl = "u",
                SkillFolderHash = "1"
            },
            TestContext.Current.CancellationToken);

        current = secondTime;
        await lockFile.AddEntryAsync(
            "my-skill",
            new SkillLockEntry
            {
                Source = "org/repo",
                SourceType = "github",
                SourceUrl = "u",
                SkillFolderHash = "2"
            },
            TestContext.Current.CancellationToken);

        var entry = await lockFile.GetEntryAsync("my-skill", TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.Equal(firstTime.ToString("o"), entry!.InstalledAt);
        Assert.Equal(secondTime.ToString("o"), entry.UpdatedAt);
        Assert.Equal("2", entry.SkillFolderHash);
    }

    [Fact]
    public async Task RemoveEntryAsync_Removes_Existing_Skill()
    {
        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);
        await lockFile.AddEntryAsync(
            "my-skill",
            new SkillLockEntry
            {
                Source = "org/repo",
                SourceType = "github",
                SourceUrl = "u",
                SkillFolderHash = "h"
            },
            TestContext.Current.CancellationToken);

        var removed = await lockFile.RemoveEntryAsync("my-skill", TestContext.Current.CancellationToken);

        Assert.True(removed);
        var entry = await lockFile.GetEntryAsync("my-skill", TestContext.Current.CancellationToken);
        Assert.Null(entry);
    }

    [Fact]
    public async Task RemoveEntryAsync_Returns_False_For_Missing_Skill()
    {
        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);

        var removed = await lockFile.RemoveEntryAsync("nothing", TestContext.Current.CancellationToken);

        Assert.False(removed);
    }

    [Fact]
    public async Task ReadAsync_Wipes_On_Old_Version()
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await File.WriteAllTextAsync(
            lockPath,
            """
            {
              "version": 1,
              "skills": {
                "old-skill": {
                  "source": "org/repo",
                  "sourceType": "github",
                  "sourceUrl": "u",
                  "skillFolderHash": "h",
                  "installedAt": "2024-01-01T00:00:00Z",
                  "updatedAt": "2024-01-01T00:00:00Z"
                }
              }
            }
            """,
            TestContext.Current.CancellationToken);

        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);
        var result = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GlobalLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task ReadAsync_Returns_Empty_On_Corrupted_Json()
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await File.WriteAllTextAsync(lockPath, "{ this is not json", TestContext.Current.CancellationToken);

        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);
        var result = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GlobalLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task ReadWrite_Round_Trip_Preserves_Entries()
    {
        var lockFile = new GlobalLockFile(_xdgPaths, () => FixedNow);
        var input = new SkillLockFile
        {
            Version = GlobalLockFile.CurrentVersion,
            Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
            {
                ["round-trip"] = new()
                {
                    Source = "org/repo",
                    SourceType = "github",
                    SourceUrl = "https://github.com/org/repo.git",
                    Ref = "main",
                    SkillPath = "skills/round-trip",
                    SkillFolderHash = "deadbeef",
                    InstalledAt = "2025-01-15T12:00:00Z",
                    UpdatedAt = "2025-01-15T12:00:00Z"
                }
            }
        };

        await lockFile.WriteAsync(input, TestContext.Current.CancellationToken);
        var output = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GlobalLockFile.CurrentVersion, output.Version);
        var entry = output.Skills["round-trip"];
        Assert.Equal("org/repo", entry.Source);
        Assert.Equal("github", entry.SourceType);
        Assert.Equal("https://github.com/org/repo.git", entry.SourceUrl);
        Assert.Equal("main", entry.Ref);
        Assert.Equal("skills/round-trip", entry.SkillPath);
        Assert.Equal("deadbeef", entry.SkillFolderHash);
    }

    [Fact]
    public void XdgPaths_Resolves_GlobalLockPath_From_XdgStateHome()
    {
        var stateDir = Path.Combine(_tempDir, "state");

        Assert.Equal(Path.Combine(stateDir, "skills", ".skill-lock.json"), _xdgPaths.GetGlobalLockPath());
    }

    [Fact]
    public void XdgPaths_Falls_Back_To_AgentsDir_When_No_XdgStateHome()
    {
        var home = Path.Combine(_tempDir, "home");
        Directory.CreateDirectory(home);
        var paths = new XdgPaths(home, _ => null);

        Assert.Equal(Path.Combine(home, ".agents", ".skill-lock.json"), paths.GetGlobalLockPath());
    }

    [Fact]
    public async Task AddEntryAsync_Creates_Directory_If_Missing()
    {
        var nestedHome = Path.Combine(_tempDir, "deeply", "nested", "home");
        var paths = new XdgPaths(nestedHome, _ => null);
        var lockFile = new GlobalLockFile(paths, () => FixedNow);

        await lockFile.AddEntryAsync(
            "first-skill",
            new SkillLockEntry
            {
                Source = "org/repo",
                SourceType = "github",
                SourceUrl = "u",
                SkillFolderHash = "h"
            },
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(paths.GetGlobalLockPath()));
    }
}
