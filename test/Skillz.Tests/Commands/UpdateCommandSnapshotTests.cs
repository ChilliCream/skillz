using CookieCrumble;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class UpdateCommandSnapshotTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public UpdateCommandSnapshotTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-update-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        _originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_workspace);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    // Redirect input so progress is rendered as plain dim lines instead of raw TTY escape sequences.
    private static IServiceProvider BuildServices()
        => CliTestHelper.CreateServiceProvider(configure: services =>
            services.AddSingleton<ConsoleEnvironment>(new TestConsoleEnvironment { InputRedirected = true }));

    private static SkillLockEntry GlobalEntry(string folderHash) => new()
    {
        Source = "owner/repo",
        SourceType = "github",
        SourceUrl = "https://github.com/owner/repo",
        SkillFolderHash = folderHash,
        SkillPath = "skills/my-skill/SKILL.md"
    };

    [Fact]
    public async Task Update_Global_With_No_Skills()
    {
        // Arrange
        var services = BuildServices();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile { Version = 3, Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal) };

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-g");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -g

            Checking for skill updates...

            No global skills tracked in lock file.
            Install skills with: skillz add <package> -g
            """);
    }

    [Fact]
    public async Task Update_Global_Up_To_Date()
    {
        // Arrange
        var services = BuildServices();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["my-skill"] = GlobalEntry("abc123")
                }
            };
        var blob = services.GetRequiredService<TestBlobClient>();
        blob.OnFetchTree = (_, _, _, _) =>
            new RepoTree("tree-sha", "main", [new TreeEntry { Path = "skills/my-skill", Type = "tree", Sha = "abc123" }]);

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-g");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -g

            Checking for skill updates...

            Checking global skill 1/1: my-skill
            All global skills are up to date
            """);
    }

    [Fact]
    public async Task Update_Global_Update_Available()
    {
        // Arrange
        var services = BuildServices();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["my-skill"] = GlobalEntry("abc123")
                }
            };
        var blob = services.GetRequiredService<TestBlobClient>();
        blob.OnFetchTree = (_, _, _, _) =>
            new RepoTree("new-sha", "main", [new TreeEntry { Path = "skills/my-skill", Type = "tree", Sha = "xyz789" }]);

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-g");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -g

            Checking for skill updates...

            Checking global skill 1/1: my-skill
            Found 1 global update(s)

            Update available: my-skill
              Run: skillz add owner/repo/skills/my-skill -g -y

            Updates available for 1 skill(s); no updates were applied.
            """);
    }

    [Fact]
    public async Task Update_Global_Skips_Skill_Without_Folder_Hash()
    {
        // Arrange
        var services = BuildServices();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["legacy"] = GlobalEntry(string.Empty)
                }
            };

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-g");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -g

            Checking for skill updates...


            1 skill(s) cannot be checked automatically:
              * legacy (Private or deleted repo)
                To update: skillz add https://github.com/owner/repo -g -y
            """);
    }

    [Fact]
    public async Task Update_Project_With_No_Skills()
    {
        // Arrange
        var services = BuildServices();
        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-p");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -p

            Checking for skill updates...

            No project skills to update.
            Install project skills with: skillz add <package>
            """);
    }

    [Fact]
    public async Task Update_Project_Update_Available()
    {
        // Arrange
        var services = BuildServices();
        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
            {
                ["my-skill"] = new LocalSkillLockEntry
                {
                    Source = "owner/repo",
                    SourceType = "github",
                    Ref = "main",
                    SkillPath = "skills/my-skill/SKILL.md",
                    ComputedHash = "hash"
                }
            }
        };

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-p");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -p

            Checking for skill updates...

            1 project skill(s) can be refreshed (re-install to update):

            Refresh: my-skill
              Run: skillz add owner/repo/skills/my-skill#main --skill my-skill -y
            """);
    }

    [Fact]
    public async Task Update_Both_Scopes_Show_Headers()
    {
        // Arrange
        var services = BuildServices();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile { Version = 3, Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal) };
        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };

        // Act
        var output = await CommandSnapshot.RunAsync(services, "update", "-g", "-p");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz update -g -p

            Checking for skill updates...

            Global Skills
            No global skills tracked in lock file.
            Install skills with: skillz add <package> -g

            Project Skills
            No project skills to update.
            Install project skills with: skillz add <package>
            """);
    }
}
