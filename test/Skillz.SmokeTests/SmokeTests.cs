using System.Runtime.CompilerServices;
using CliWrap;
using CliWrap.Buffered;
using Xunit;

namespace Skillz.SmokeTests;

public class SmokeTests
{
    private static readonly string ProjectPath = ResolveProjectPath();

    [Fact]
    public async Task Version_Flag_Prints_Version()
    {
        var result = await RunSkillzAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task Help_Flag_Prints_Usage()
    {
        var result = await RunSkillzAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput);
        Assert.Contains("skillz", result.StandardOutput);
        Assert.Contains("add", result.StandardOutput);
        Assert.Contains("remove", result.StandardOutput);
        Assert.Contains("list", result.StandardOutput);
        Assert.Contains("init", result.StandardOutput);
    }

    [Fact]
    public async Task Init_Creates_SkillMd_In_Named_Directory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var result = await RunSkillzAsync("init test-skill", workingDirectory: tempDir);

            Assert.Equal(0, result.ExitCode);
            var skillFile = Path.Combine(tempDir, "test-skill", "SKILL.md");
            Assert.True(File.Exists(skillFile), $"Expected SKILL.md at {skillFile}");

            var content = await File.ReadAllTextAsync(skillFile, TestContext.Current.CancellationToken);
            Assert.Contains("name: test-skill", content);
            Assert.Contains("description:", content);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task List_Does_Not_Crash_In_Empty_Directory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var result = await RunSkillzAsync("list", workingDirectory: tempDir);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    private static async Task<BufferedCommandResult> RunSkillzAsync(string arguments, string? workingDirectory = null)
    {
        var args = new[] { "run", "--project", ProjectPath, "--framework", "net9.0", "--no-build", "--" }.Concat(
            SplitArguments(arguments));

        var command = Cli.Wrap("dotnet").WithArguments(args).WithValidation(CommandResultValidation.None);

        if (workingDirectory is not null)
        {
            command = command.WithWorkingDirectory(workingDirectory);
        }

        return await command.ExecuteBufferedAsync(TestContext.Current.CancellationToken);
    }

    private static IEnumerable<string> SplitArguments(string arguments)
    {
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "skillz-smoke-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch { }
    }

    private static string ResolveProjectPath([CallerFilePath] string? sourceFilePath = null)
    {
        var dir =
            Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Unable to resolve smoke test source directory.");
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "src", "Skillz", "Skillz.csproj"));
    }
}
