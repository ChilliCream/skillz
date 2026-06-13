using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Skills;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class RemoveCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public RemoveCommandTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-remove-" + Guid.NewGuid().ToString("N"));
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

    private static void CreateSkill(string baseDir, string name)
    {
        var dir = Path.Combine(baseDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: {name}\ndescription: x\n---\n");
    }

    private void ConfigureInstaller(TestInstaller installer)
    {
        installer.OnGetCanonicalSkillsDir = (_, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills");

        installer.OnGetCanonicalPath = (name, _, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills", name);

        installer.OnGetAgentBaseDir = (_, _, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills");

        installer.OnGetInstallPath = (name, _, _, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills", name);
    }

    [Fact]
    public async Task Remove_With_No_Skills_Reports_Empty()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        ConfigureInstaller(installer);

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var parseResult = cmd.Parse(["--yes"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        var interaction = services.GetRequiredService<CapturingConsole>();
        Assert.Contains("No skills", interaction.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remove_With_Named_Skill_Deletes_Directory_And_Updates_Lock()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        CreateSkill(canonical, "beta");

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        ConfigureInstaller(installer);

        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        var removedFromLock = new List<string>();
        projectLock.OnRemoveEntry = (name, _) =>
        {
            removedFromLock.Add(name);
            return true;
        };

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var parseResult = cmd.Parse(["alpha", "--yes"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(canonical, "alpha")));
        Assert.True(Directory.Exists(Path.Combine(canonical, "beta")));
        Assert.Contains("alpha", removedFromLock);
    }

    [Fact]
    public async Task Remove_With_All_Flag_Removes_Everything_Without_Prompts()
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
        var cmd = services.GetRequiredService<RemoveCommand>();
        var parseResult = cmd.Parse(["--all"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(canonical, "alpha")));
        Assert.False(Directory.Exists(Path.Combine(canonical, "beta")));
    }

    [Fact]
    public async Task Remove_Reports_Failure_When_OnDiskFolder_Does_Not_Map_To_Sanitized_Path()
    {
        // Arrange: an externally-created, unsanitized folder ("My Skill") is on disk. The
        // installer resolves install/canonical paths from the SANITIZED name ("my-skill"),
        // which does not exist - so nothing is actually deleted. The command must not claim
        // success when nothing was removed.
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        var onDiskFolder = Path.Combine(canonical, "My Skill");
        CreateSkill(canonical, "My Skill");

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        installer.OnGetCanonicalSkillsDir = (_, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills");
        installer.OnGetAgentBaseDir = (_, _, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills");
        installer.OnGetCanonicalPath = (name, _, cwd) =>
            Path.Combine(cwd ?? _workspace, ".agents", "skills", SkillNameSanitizer.SanitizeName(name));
        installer.OnGetInstallPath = (name, _, _, cwd) =>
            Path.Combine(cwd ?? _workspace, ".agents", "skills", SkillNameSanitizer.SanitizeName(name));

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var parseResult = cmd.Parse(["--all"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: nothing was deleted and the command reported a failure (did not claim success).
        Assert.NotEqual(0, exitCode);
        Assert.True(Directory.Exists(onDiskFolder));
        var interaction = services.GetRequiredService<CapturingConsole>();
        Assert.DoesNotContain("Successfully removed", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Failed to remove", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_Uses_Interactive_Selection_When_Interactive()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        CreateSkill(canonical, "beta");

        // Drive the real prompts over an interactive console: the multiselect lists [alpha, beta]; move
        // down to beta, toggle it, confirm, then answer the removal confirmation with "y".
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.DownArrow); // alpha -> beta
        console.Input.PushKey(ConsoleKey.Spacebar); // select beta
        console.Input.PushKey(ConsoleKey.Enter); // confirm selection
        console.Input.PushTextWithEnter("y"); // confirm removal

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true, configure: s =>
        {
            s.AddSingleton<ConsoleEnvironment>(new TestConsoleEnvironment { InputRedirected = false });
            s.AddSingleton<IAnsiConsole>(console);
        });

        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        ConfigureInstaller(installer);

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(Path.Combine(canonical, "alpha")));
        Assert.False(Directory.Exists(Path.Combine(canonical, "beta")));
    }

    [Fact]
    public async Task Remove_Cancels_Gracefully_When_Output_NonInteractive()
    {
        // Arrange: stdin is a TTY (IsInputRedirected=false) so the command tries to prompt, but the
        // console cannot drive the key loop (the default CapturingConsole is non-interactive, as a
        // redirected stdout would be). The multi-select must degrade to "cancelled", not crash Spectre.
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        CreateSkill(canonical, "beta");

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        ConfigureInstaller((TestInstaller)services.GetRequiredService<ISkillInstaller>());

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var exitCode = await cmd.Parse(Array.Empty<string>())
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: graceful cancellation, nothing removed.
        Assert.Equal(ExitCodeConstants.Cancelled, exitCode);
        Assert.Contains(
            "Removal cancelled",
            services.GetRequiredService<CapturingConsole>().OutputText,
            StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(canonical, "alpha")));
        Assert.True(Directory.Exists(Path.Combine(canonical, "beta")));
    }

    // Removal is destructive: when a NAMED skill is given but the console cannot show the confirm
    // (redirected stream -> not Interactive; TERM=dumb -> not Ansi), the user must pass -y. Otherwise
    // WithDefault DECLINES so nothing is silently deleted.
    // The three capability combinations Spectre cannot show a prompt on (anything but Interactive+Ansi):
    [Theory]
    [InlineData(false, false)] // fully headless (stdin redirected, no ANSI)
    [InlineData(true, false)] // real TTY with TERM=dumb (no ANSI)
    [InlineData(false, true)] // stdin redirected but ANSI-capable (e.g. `echo y | skillz remove ...`)
    public async Task Remove_Named_Skill_Declines_When_Console_CannotPrompt(bool interactive, bool ansi)
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");

        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = interactive;
        console.Profile.Capabilities.Ansi = ansi;
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true, configure: s =>
            s.AddSingleton<IAnsiConsole>(console));
        ConfigureInstaller((TestInstaller)services.GetRequiredService<ISkillInstaller>());

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var exitCode = await cmd.Parse(["alpha"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: declined -> cancelled, skill untouched (no silent destructive delete).
        Assert.Equal(ExitCodeConstants.Cancelled, exitCode);
        Assert.True(Directory.Exists(Path.Combine(canonical, "alpha")));
        Assert.Contains("Removal cancelled", console.Output, StringComparison.Ordinal);
    }
}
