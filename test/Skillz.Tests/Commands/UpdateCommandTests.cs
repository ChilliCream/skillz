using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Spectre.Console;
using Spectre.Console.Testing;
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("No global skills tracked", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("up to date", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("Found 1 global update", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Update available", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Updates available for 1 skill", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("Updated 1 skill", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_Global_Sanitizes_Install_Source_When_SkillPath_Contains_Terminal_Escape()
    {
        // Arrange: a SkillPath whose directory name contains a raw terminal escape sequence. The
        // byte-exact folder must still match the tree entry path so an update is reported, while the
        // printed "Run: skillz add ..." suggestion must be escape-free.
        const string escapedFolder = "tools/inner\x1b]0;title\x07";
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
                        SkillPath = $"{escapedFolder}/SKILL.md"
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
                        // Byte-exact match against the un-sanitized derived folder so the update is detected.
                        Path = escapedFolder,
                        Type = "tree",
                        Sha = "xyz789"
                    }
                ]);
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the update is still detected (tree matching uses the byte-exact path)...
        Assert.Equal(0, exit);
        Assert.Contains("Found 1 global update", interaction.OutputText, StringComparison.Ordinal);

        // ...but no raw ESC (0x1b) byte reaches the terminal output, while the visible
        // suggestion line is still shown.
        Assert.DoesNotContain('\x1b', interaction.OutputText);
        Assert.Contains(
            "Run: skillz add owner/repo/tools/inner",
            interaction.OutputText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_Global_Reports_Timeout_Distinct_From_Missing_When_Fetch_Times_Out()
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
        blob.OnFetchTree = (_, _, _) => throw new BlobFetchTimeoutException("https://api.github.com/owner/repo");
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: a timeout is reported as a timeout, not bucketed under the missing/access-error message.
        Assert.Equal(0, exit);
        Assert.Contains("timed out", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("network or access error", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Failed to check 1 skill", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_Global_Reports_Missing_When_Fetch_Returns_Null()
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
        blob.OnFetchTree = (_, _, _) => null;
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: a missing/unreachable repo uses the network-or-access-error bucket, not the timeout one.
        Assert.Equal(0, exit);
        Assert.Contains("network or access error", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("timed out", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("cannot be checked automatically", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("No project skills", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("can be refreshed", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Refresh:", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("Updates available for", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("No project skills", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_With_Yes_Flag_And_No_Scope_Checks_Both_Scopes()
    {
        // Arrange: -y without -g/-p is non-interactive and ambiguous. The command must not silently
        // pick one scope (and risk checking the wrong one); it checks BOTH global and project.
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-y"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: both scopes were checked, so both empty-scope messages appear.
        Assert.Equal(0, exit);
        Assert.Contains("No global skills", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("No project skills", interaction.OutputText, StringComparison.Ordinal);
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
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g", "-p"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("Global Skills", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Project Skills", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_With_Yes_Flag_Does_Not_Silently_Skip_Project_Skills()
    {
        // Arrange: a project whose skills live in agent-specific dirs leaves nothing the command
        // tracks for a "has project skills?" heuristic to find (no project lock entry would be
        // detectable up front). The non-interactive default must still check project skills rather
        // than silently resolving to Global only and missing them. Global has a real update so we
        // can prove BOTH scopes ran in the same invocation.
        var services = CliTestHelper.CreateServiceProvider();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        globalLock.OnRead = () =>
            new SkillLockFile
            {
                Version = 3,
                Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal)
                {
                    ["global-skill"] = new SkillLockEntry
                    {
                        Source = "owner/repo",
                        SourceType = "github",
                        SourceUrl = "https://github.com/owner/repo",
                        SkillFolderHash = "old",
                        SkillPath = "skills/global-skill/SKILL.md"
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
                        Path = "skills/global-skill",
                        Type = "tree",
                        Sha = "new"
                    }
                ]);

        var projectLock = services.GetRequiredService<TestProjectLockFile>();
        projectLock.OnRead = _ => new LocalSkillLockFile
        {
            Version = 1,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
            {
                ["project-skill"] = new LocalSkillLockEntry
                {
                    Source = "owner/repo",
                    SourceType = "github",
                    Ref = "main",
                    SkillPath = "skills/project-skill/SKILL.md",
                    ComputedHash = "hash"
                }
            }
        };
        var interaction = services.GetRequiredService<CapturingConsole>();

        // Act: non-interactive, no scope flag.
        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-y"]);
        var exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: project skills were NOT silently skipped, and global was checked too.
        Assert.Equal(0, exit);
        Assert.Contains("Refresh: project-skill", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Update available: global-skill", interaction.OutputText, StringComparison.Ordinal);
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

    // A redirected/headless stream is not Interactive; a real TTY with TERM=dumb is Interactive but
    // not Ansi. Both make the "Update scope" picker unshowable, so it must default to Both, not crash.
    [Theory]
    [InlineData(false, false)] // fully headless (stdin redirected, no ANSI)
    [InlineData(true, false)] // real TTY with TERM=dumb (no ANSI)
    [InlineData(false, true)] // stdin redirected but ANSI-capable
    public async Task Update_DefaultsToBothScopes_When_NoFlag_And_Console_CannotPrompt(bool interactive, bool ansi)
    {
        // Arrange: no scope flag, no -y, stdin not redirected -> ResolveScopeAsync reaches the scope
        // SelectPrompt. With no tracked skills both scopes simply report "none".
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = interactive;
        console.Profile.Capabilities.Ansi = ansi;
        var services = CliTestHelper.CreateServiceProvider(
            configure: s => s.AddSingleton<IAnsiConsole>(console));

        // Act
        var cmd = services.GetRequiredService<UpdateCommand>();
        var exitCode = await cmd.Parse(Array.Empty<string>())
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: degraded to UpdateScope.Both (both scopes checked) instead of crashing.
        Assert.Equal(0, exitCode);
        Assert.Contains("No global skills", console.Output, StringComparison.Ordinal);
        Assert.Contains("No project skills", console.Output, StringComparison.Ordinal);
    }
}
