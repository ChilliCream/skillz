using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

// In the collection so the Console.SetOut redirection used to capture the in-place progress line
// does not race with other Console-touching command tests.
[Collection(CommandTestCollection.Name)]
public class UpdateCommandProgressTests
{
    // The clear sequence the command writes to wipe the in-place progress line.
    private const string ClearLine = "\r\x1b[K";

    [Fact]
    public async Task Update_Clears_Progress_Line_When_Loop_Is_Cancelled_MidCheck()
    {
        // Arrange: a TTY (not redirected) global check with a checkable skill, so the command draws
        // the in-place "\r\x1b[K{progress}" line. The fetch cancels and throws partway through, which
        // must still leave a cleared line — not a stale partial progress line — on the terminal.
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

        using var cts = new CancellationTokenSource();
        var blob = services.GetRequiredService<TestBlobClient>();
        blob.OnFetchTree = (_, _, _) =>
        {
            // Simulate a genuine user cancellation arriving while the loop is running.
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);

        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        Console.SetOut(stdout);

        try
        {
            // The System.CommandLine pipeline catches the cancellation and turns it into a non-zero
            // exit code rather than rethrowing, but the command's own try/finally runs first.
            await parseResult.InvokeAsync(cancellationToken: cts.Token);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert: the progress line was drawn and then cleared despite the mid-loop cancellation, so
        // the last thing written to the terminal is the clear sequence — no stale partial line remains.
        var written = stdout.ToString();
        Assert.Contains("Checking global skill 1/1", written, StringComparison.Ordinal);
        Assert.EndsWith(ClearLine, written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_Clears_Progress_Line_On_Successful_Completion()
    {
        // Arrange: the normal happy path still ends with a cleared progress line (the finally runs
        // on the success path too), so the up-to-date summary is not preceded by stale progress text.
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

        var cmd = services.GetRequiredService<UpdateCommand>();
        var parseResult = cmd.Parse(["-g"]);

        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        Console.SetOut(stdout);

        int exit;
        try
        {
            exit = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert: progress was drawn and then cleared.
        Assert.Equal(0, exit);
        var written = stdout.ToString();
        Assert.Contains("Checking global skill 1/1", written, StringComparison.Ordinal);
        Assert.EndsWith(ClearLine, written, StringComparison.Ordinal);
    }
}
