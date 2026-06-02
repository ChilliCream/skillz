using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CliWrap;
using CliWrap.Buffered;
using Xunit;

namespace Skillz.SmokeTests;

public class SmokeTests
{
    private static readonly string ProjectPath = ResolveProjectPath();
    private static readonly string TargetFramework = ResolveTargetFramework();
    private static readonly string Configuration = ResolveConfiguration();

    [Fact]
    public async Task Version_Flag_Prints_Version()
    {
        // Act
        var result = await RunSkillzAsync("--version");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task Help_Flag_Prints_Usage()
    {
        // Act
        var result = await RunSkillzAsync("--help");

        // Assert
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
        // Arrange
        var tempDir = CreateTempDirectory();
        try
        {
            // Act
            var result = await RunSkillzAsync("init test-skill", workingDirectory: tempDir);

            // Assert
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
        // Arrange
        var tempDir = CreateTempDirectory();
        try
        {
            // Act
            var result = await RunSkillzAsync("list", workingDirectory: tempDir);

            // Assert
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    private static async Task<BufferedCommandResult> RunSkillzAsync(string arguments, string? workingDirectory = null)
    {
        // Run the already-built CLI in the SAME configuration these tests were
        // built in. 'dotnet run --no-build' defaults to Debug, so without this
        // a Release test run (e.g. CI) would point at a Debug build that does
        // not exist and every smoke test would fail with exit code 1.
        var args = new[]
        {
            "run", "--project", ProjectPath, "-c", Configuration, "--framework", TargetFramework, "--no-build", "--"
        }.Concat(SplitArguments(arguments));

        var command = Cli.Wrap("dotnet").WithArguments(args).WithValidation(CommandResultValidation.None);

        if (workingDirectory is not null)
        {
            command = command.WithWorkingDirectory(workingDirectory);
        }

        return await command.ExecuteBufferedAsync(TestContext.Current.CancellationToken);
    }

    private static string ResolveConfiguration()
    {
        var configured = Environment.GetEnvironmentVariable("SKILLZ_SMOKE_TEST_CONFIGURATION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string ResolveTargetFramework()
    {
        var configuredFramework = Environment.GetEnvironmentVariable("SKILLZ_SMOKE_TEST_TFM");
        if (!string.IsNullOrWhiteSpace(configuredFramework))
        {
            return configuredFramework.Trim();
        }

        var frameworkName =
            typeof(SmokeTests).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), inherit: false)
                .OfType<TargetFrameworkAttribute>()
                .Single()
                .FrameworkName;

        var versionPrefix = ".NETCoreApp,Version=v";
        if (!frameworkName.StartsWith(versionPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported target framework '{frameworkName}'.");
        }

        return "net" + frameworkName[versionPrefix.Length..];
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
