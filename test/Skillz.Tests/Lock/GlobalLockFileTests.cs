using Skillz;
using Skillz.Install;
using Skillz.Locking;
using Skillz.Tests.TestServices;
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
        _xdgPaths = new XdgPaths(new FakeSystemEnvironment
        {
            HomeDirectory = _tempDir,
            Env = { ["XDG_STATE_HOME"] = stateDir }
        });
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
        // Arrange
        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act
        var result = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GlobalLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task AddEntryAsync_Stores_Skill_With_InstalledAt_And_UpdatedAt()
    {
        // Arrange
        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act
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

        // Assert
        var entry = await lockFile.FindEntryAsync("my-skill", TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.Equal("org/repo", entry!.Source);
        Assert.Equal(FixedNow.ToString("o"), entry.InstalledAt);
        Assert.Equal(FixedNow.ToString("o"), entry.UpdatedAt);
    }

    [Fact]
    public async Task AddEntryAsync_Preserves_InstalledAt_On_Update()
    {
        // Arrange
        var firstTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondTime = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(firstTime);

        var lockFile = new GlobalLockFile(_xdgPaths, clock);

        // Act
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

        clock.UtcNow = secondTime;
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

        // Assert
        var entry = await lockFile.FindEntryAsync("my-skill", TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.Equal(firstTime.ToString("o"), entry!.InstalledAt);
        Assert.Equal(secondTime.ToString("o"), entry.UpdatedAt);
        Assert.Equal("2", entry.SkillFolderHash);
    }

    [Fact]
    public async Task RemoveEntryAsync_Removes_Existing_Skill()
    {
        // Arrange
        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));
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

        // Act
        var removed = await lockFile.RemoveEntryAsync("my-skill", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removed);
        var entry = await lockFile.FindEntryAsync("my-skill", TestContext.Current.CancellationToken);
        Assert.Null(entry);
    }

    [Fact]
    public async Task RemoveEntryAsync_Returns_False_For_Missing_Skill()
    {
        // Arrange
        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act
        var removed = await lockFile.RemoveEntryAsync("nothing", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public async Task ReadAsync_Wipes_On_Old_Version()
    {
        // Arrange
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

        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act
        var result = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GlobalLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task ReadAsync_Returns_Empty_On_Corrupted_Json()
    {
        // Arrange
        var lockPath = _xdgPaths.GetGlobalLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await File.WriteAllTextAsync(lockPath, "{ this is not json", TestContext.Current.CancellationToken);

        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act
        var result = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GlobalLockFile.CurrentVersion, result.Version);
        Assert.Empty(result.Skills);
        // A plain read must not rewrite or wipe a corrupt file on disk.
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(lockPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddEntryAsync_Refuses_Corrupted_Json_And_Preserves_File()
    {
        // Arrange
        var lockPath = _xdgPaths.GetGlobalLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        const string corrupt = "{ this is not json";
        await File.WriteAllTextAsync(lockPath, corrupt, TestContext.Current.CancellationToken);

        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CliException>(() => lockFile.AddEntryAsync(
            "new-skill",
            new SkillLockEntry { Source = "org/repo", SourceType = "github", SourceUrl = "u", SkillFolderHash = "h" },
            TestContext.Current.CancellationToken));

        Assert.Contains("corrupt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(lockPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddEntryAsync_Refuses_Zero_Byte_Lock_And_Preserves_File()
    {
        // Arrange
        var lockPath = _xdgPaths.GetGlobalLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await File.WriteAllBytesAsync(lockPath, [], TestContext.Current.CancellationToken);

        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CliException>(() => lockFile.AddEntryAsync(
            "new-skill",
            new SkillLockEntry { Source = "org/repo", SourceType = "github", SourceUrl = "u", SkillFolderHash = "h" },
            TestContext.Current.CancellationToken));

        Assert.Contains("corrupt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, new FileInfo(lockPath).Length);
    }

    [Fact]
    public async Task AddEntryAsync_Rejects_Newer_Lock_Version_And_Preserves_File()
    {
        // Arrange
        var lockPath = _xdgPaths.GetGlobalLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var newer = $$"""
            {
              "version": {{GlobalLockFile.CurrentVersion + 1}},
              "skills": {}
            }
            """;
        await File.WriteAllTextAsync(lockPath, newer, TestContext.Current.CancellationToken);

        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CliException>(() => lockFile.AddEntryAsync(
            "new-skill",
            new SkillLockEntry { Source = "org/repo", SourceType = "github", SourceUrl = "u", SkillFolderHash = "h" },
            TestContext.Current.CancellationToken));

        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(newer, await File.ReadAllTextAsync(lockPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadWrite_Round_Trip_Preserves_Entries()
    {
        // Arrange
        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));
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

        // Act
        await lockFile.WriteAsync(input, TestContext.Current.CancellationToken);
        var output = await lockFile.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GlobalLockFile.CurrentVersion, output.Version);
        var entry = output.Skills["round-trip"];
        Assert.Equal("org/repo", entry.Source);
        Assert.Equal("github", entry.SourceType);
        Assert.Equal("https://github.com/org/repo.git", entry.SourceUrl);
        Assert.Equal("main", entry.Ref);
        Assert.Equal("skills/round-trip", entry.SkillPath);
        Assert.Equal("deadbeef", entry.SkillFolderHash);
        Assert.False(File.Exists(_xdgPaths.GetGlobalLockPath() + ".tmp"));
    }

    [Fact]
    public async Task WriteAsync_Replaces_Existing_File_Without_Leaving_Temp_File()
    {
        // Arrange
        var lockFile = new GlobalLockFile(_xdgPaths, new FakeTimeProvider(FixedNow));
        await lockFile.AddEntryAsync(
            "old-skill",
            new SkillLockEntry { Source = "org/old", SourceType = "github", SourceUrl = "u", SkillFolderHash = "old" },
            TestContext.Current.CancellationToken);

        var replacement = new SkillLockFile
        {
            Version = GlobalLockFile.CurrentVersion,
            Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
            {
                ["new-skill"] = new()
                {
                    Source = "org/new",
                    SourceType = "github",
                    SourceUrl = "u",
                    SkillFolderHash = "new"
                }
            }
        };

        // Act
        await lockFile.WriteAsync(replacement, TestContext.Current.CancellationToken);

        // Assert
        var output = await lockFile.ReadAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("old-skill", output.Skills.Keys);
        Assert.Equal("new", output.Skills["new-skill"].SkillFolderHash);
        Assert.False(File.Exists(_xdgPaths.GetGlobalLockPath() + ".tmp"));
    }

    [Fact]
    public void XdgPaths_Resolves_GlobalLockPath_From_XdgStateHome()
    {
        // Arrange
        var stateDir = Path.Combine(_tempDir, "state");

        // Act & Assert
        Assert.Equal(Path.Combine(stateDir, "skills", ".skill-lock.json"), _xdgPaths.GetGlobalLockPath());
    }

    [Fact]
    public void XdgPaths_Falls_Back_To_AgentsDir_When_No_XdgStateHome()
    {
        // Arrange
        var home = Path.Combine(_tempDir, "home");
        Directory.CreateDirectory(home);
        var paths = new XdgPaths(new FakeSystemEnvironment { HomeDirectory = home });

        // Act & Assert
        Assert.Equal(Path.Combine(home, ".agents", ".skill-lock.json"), paths.GetGlobalLockPath());
    }

    [Fact]
    public async Task AddEntryAsync_Creates_Directory_If_Missing()
    {
        // Arrange
        var nestedHome = Path.Combine(_tempDir, "deeply", "nested", "home");
        var paths = new XdgPaths(new FakeSystemEnvironment { HomeDirectory = nestedHome });
        var lockFile = new GlobalLockFile(paths, new FakeTimeProvider(FixedNow));

        // Act
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

        // Assert
        Assert.True(File.Exists(paths.GetGlobalLockPath()));
    }
}
