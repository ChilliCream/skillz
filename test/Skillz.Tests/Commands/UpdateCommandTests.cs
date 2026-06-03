using Microsoft.Extensions.DependencyInjection;
using Skillz;
using Skillz.Commands;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

public class UpdateCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public UpdateCommandTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-update-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Update_Global_Scope_With_No_Skills_Prints_Empty_Message()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile { Version = 3, Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal) };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(
            interaction.Output,
            line => line.Contains("No global skills tracked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Global_Reports_Up_To_Date_When_Hash_Matches()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["my-skill"] = new SkillLockEntry
                    {
                        Source = "owner/repo",
                        SourceType = "github",
                        SourceUrl = "https://github.com/owner/repo",
                        SkillFolderHash = "abc123",
                        SkillPath = "skills/my-skill/SKILL.md"
                    }
                }
            };

        var blob = services.GetRequiredService<TestBlobClient>();
        blob.OnFetchTree = (_, _, _) =>
            new RepoTree(
                "tree-sha",
                "main",
                [
                    new TreeEntry
                    {
                        Path = "skills/my-skill",
                        Type = "tree",
                        Sha = "abc123"
                    }
                ]);
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("up to date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Global_Reports_Update_Available_When_Hash_Differs()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["my-skill"] = new SkillLockEntry
                    {
                        Source = "owner/repo",
                        SourceType = "github",
                        SourceUrl = "https://github.com/owner/repo",
                        SkillFolderHash = "abc123",
                        SkillPath = "skills/my-skill/SKILL.md"
                    }
                }
            };

        var blob = services.GetRequiredService<TestBlobClient>();
        blob.OnFetchTree = (_, _, _) =>
            new RepoTree(
                "new-tree-sha",
                "main",
                [
                    new TreeEntry
                    {
                        Path = "skills/my-skill",
                        Type = "tree",
                        Sha = "xyz789"
                    }
                ]);
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("Found 1 global update", StringComparison.Ordinal));
        Assert.Contains(interaction.Output, line => line.Contains("Update available", StringComparison.Ordinal));
        Assert.Contains(interaction.Output, line => line.Contains("Updates available for 1 skill", StringComparison.Ordinal));
        Assert.DoesNotContain(interaction.Output, line => line.Contains("Updated 1 skill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Global_Skips_Skills_Without_Folder_Hash()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["legacy"] = new SkillLockEntry
                    {
                        Source = "owner/repo",
                        SourceType = "github",
                        SourceUrl = "https://github.com/owner/repo",
                        SkillFolderHash = string.Empty,
                        SkillPath = "skills/legacy/SKILL.md"
                    }
                }
            };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(
            interaction.Output,
            line => line.Contains("cannot be checked automatically", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Project_With_No_Skills_Prints_Empty_Message()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("No project skills", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Project_Reports_Update_For_Trackable_Skills()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
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
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("can be refreshed", StringComparison.Ordinal));
        Assert.Contains(interaction.Output, line => line.Contains("Refresh:", StringComparison.Ordinal));
        Assert.DoesNotContain(interaction.Output, line => line.Contains("Updates available for", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Project_Skips_NodeModules_And_Local_Entries()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
            {
                ["from-package"] = new LocalSkillLockEntry
                {
                    Source = "my-pkg",
                    SourceType = "node_modules",
                    ComputedHash = "h"
                },
                ["from-local"] = new LocalSkillLockEntry
                {
                    Source = "/some/path",
                    SourceType = "local",
                    ComputedHash = "h"
                }
            }
        };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("No project skills", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_With_Yes_Flag_Auto_Detects_Scope()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile { Version = 3, Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal) };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-y"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("No global skills", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_Both_Scope_Shows_Headers()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile { Version = 3, Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal) };
        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g", "-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("Global Skills", StringComparison.Ordinal));
        Assert.Contains(interaction.Output, line => line.Contains("Project Skills", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_With_Yes_Flag_Detects_Project_Scope_Via_SystemEnvironment_CurrentDirectory()
    {
        // Arrange: seed the FakeFileStore with a project lock file under the fake env's current directory.
        // HasProjectSkillsAsync uses ISystemEnvironment.CurrentDirectory (not Directory.GetCurrentDirectory()),
        // so it must find the file via FakeFileStore regardless of the process cwd.
        var workspace = "/project-root";
        var services = CliTestHelper.CreateServiceProvider(workspace: workspace);
        var fileStore = services.GetRequiredService<FakeFileStore>();
        fileStore.Files[$"{workspace}/{KnownConfigNames.ProjectLockFileName}"] = "dummy"u8.ToArray();

        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };
        var interaction = services.GetRequiredService<TestInteractionService>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-y"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: project scope was selected (project message appears, not global)
        Assert.Equal(0, exit);
        Assert.Contains(interaction.Output, line => line.Contains("No project skills", StringComparison.Ordinal));
        Assert.DoesNotContain(interaction.Output, line => line.Contains("No global skills", StringComparison.Ordinal));
    }

    [Fact]
    public void Update_Has_Expected_Aliases()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var cmd = services.GetRequiredService<UpdateCommand>();

        // Assert
        Assert.Contains("upgrade", cmd.Aliases);
        Assert.Contains("check", cmd.Aliases);
    }
}
