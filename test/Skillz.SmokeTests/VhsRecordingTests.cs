using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CliWrap;
using CliWrap.Buffered;
using Xunit;

namespace Skillz.SmokeTests;

/// <summary>
/// End-to-end recording test. Drives the real <c>skillz</c> CLI flows (add, copy,
/// global, init, list, remove, update, and an error path) through their interactive
/// and non-interactive TUI inside the pinned VHS container (see <c>test/e2e/run.sh</c>)
/// and asserts every rendered final frame matches its committed golden. The same
/// runs also produce the demo GIFs used in PRs/README.
///
/// Heavy (publishes a binary, pulls a container, records every flow), so it is
/// opt-in: set <c>SKILLZ_E2E=1</c>. Linux + docker only; skipped otherwise. CI
/// runs it via the <c>e2e-demo</c> workflow.
/// </summary>
[Trait("Category", "E2E")]
public class VhsRecordingTests
{
    [Fact]
    public async Task AllFlows_Recordings_Match_Goldens()
    {
        if (Environment.GetEnvironmentVariable("SKILLZ_E2E") != "1")
        {
            Assert.Skip("Set SKILLZ_E2E=1 to run the VHS end-to-end recording test.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Skip("VHS recording test runs on Linux only.");
        }

        if (!IsOnPath("docker"))
        {
            Assert.Skip("docker is not available; skipping VHS recording test.");
        }

        // Act — run.sh publishes the binary, records every flow, extracts each
        // final frame, and diffs it against the golden (non-zero exit on mismatch).
        var result = await Cli.Wrap("bash")
            .WithArguments([ResolveRunScript()])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            result.ExitCode == 0,
            $"One or more VHS recordings did not match their golden (exit {result.ExitCode}).\n"
                + $"--- stdout ---\n{result.StandardOutput}\n"
                + $"--- stderr ---\n{result.StandardError}");
    }

    private static bool IsOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Any(dir =>
            {
                try
                {
                    return dir.Length > 0 && File.Exists(Path.Combine(dir, executable));
                }
                catch
                {
                    return false;
                }
            });
    }

    private static string ResolveRunScript([CallerFilePath] string? sourceFilePath = null)
    {
        var dir =
            Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Unable to resolve smoke test source directory.");
        return Path.GetFullPath(Path.Combine(dir, "..", "e2e", "run.sh"));
    }
}
