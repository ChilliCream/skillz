using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class ListCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;
    private readonly TextWriter _originalOut;
    private readonly StringWriter _capturedOut;

    public ListCommandTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        _originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_workspace);
        _originalOut = Console.Out;
        _capturedOut = new StringWriter();
        Console.SetOut(_capturedOut);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        _capturedOut.Dispose();
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

    private static void CreateSkill(string baseDir, string name)
    {
        var dir = Path.Combine(baseDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: {name}\ndescription: a test\n---\n");
    }

    private void ConfigureInstaller(TestInstaller installer)
    {
        installer.OnGetCanonicalSkillsDir = (global, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills");

        installer.OnGetAgentBaseDir = (agentType, global, cwd) =>
        {
            if (agentType == "claude-code")
            {
                return Path.Combine(cwd ?? _workspace, ".claude", "skills");
            }

            return Path.Combine(cwd ?? _workspace, ".agents", "skills");
        };
    }

    [Fact]
    public async Task List_With_No_Installed_Skills_Reports_Empty()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        ConfigureInstaller(installer);

        // Act
        var cmd = services.GetRequiredService<ListCommand>();
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        var interaction = (TestInteractionService)services.GetRequiredService<IInteractionService>();
        Assert.Contains(interaction.Output, o => o.Contains("No project skills", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_Reports_Installed_Skills_From_Canonical_Dir()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        CreateSkill(canonical, "beta");

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        ConfigureInstaller(installer);

        // Act
        var cmd = services.GetRequiredService<ListCommand>();
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        var interaction = (TestInteractionService)services.GetRequiredService<IInteractionService>();
        var output = interaction.OutputText;
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
    }

    [Fact]
    public void ShortenPath_Should_NotMangleSibling_When_PathSharesHomePrefixWithoutBoundary()
    {
        // Arrange: home is a string-prefix of the path but NOT a directory ancestor
        // (home=/home/bob, path=/home/bobby/...). The old code mangled this to "~by/...".
        var sep = Path.DirectorySeparatorChar;
        var home = $"{sep}home{sep}bob";
        var path = $"{sep}home{sep}bobby{sep}skills{sep}alpha";

        // Act
        var result = ListCommand.ShortenPath(path, home, cwd: null);

        // Assert
        Assert.Equal(path, result);
    }

    [Fact]
    public void ShortenPath_Should_NotMangleSibling_When_PathSharesCwdPrefixWithoutBoundary()
    {
        // Arrange
        var sep = Path.DirectorySeparatorChar;
        var cwd = $"{sep}work{sep}proj";
        var path = $"{sep}work{sep}project{sep}skills{sep}alpha";

        // Act
        var result = ListCommand.ShortenPath(path, home: null, cwd: cwd);

        // Assert
        Assert.Equal(path, result);
    }

    [Fact]
    public void ShortenPath_Should_ShortenToTilde_When_PathIsWithinHome()
    {
        // Arrange
        var sep = Path.DirectorySeparatorChar;
        var home = $"{sep}home{sep}bob";
        var path = $"{home}{sep}skills{sep}alpha";

        // Act
        var result = ListCommand.ShortenPath(path, home, cwd: null);

        // Assert
        Assert.Equal($"~{sep}skills{sep}alpha", result);
    }

    [Fact]
    public void ShortenPath_Should_ShortenToDot_When_PathIsWithinCwd()
    {
        // Arrange
        var sep = Path.DirectorySeparatorChar;
        var cwd = $"{sep}work{sep}proj";
        var path = $"{cwd}{sep}skills{sep}alpha";

        // Act
        var result = ListCommand.ShortenPath(path, home: null, cwd: cwd);

        // Assert
        Assert.Equal($".{sep}skills{sep}alpha", result);
    }

    [Fact]
    public async Task List_With_Json_Format_Writes_Json_To_Stdout()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        ConfigureInstaller(installer);

        // Act
        var cmd = services.GetRequiredService<ListCommand>();
        var parseResult = cmd.Parse(["--format", "json"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        var stdout = _capturedOut.ToString();
        Assert.Contains("\"name\"", stdout);
        Assert.Contains("alpha", stdout);
        Assert.Contains("\"scope\"", stdout);
    }
}
