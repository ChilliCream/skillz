using CookieCrumble;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Locking;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Spectre.Console;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class RemoveCommandSnapshotTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public RemoveCommandSnapshotTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-remove-snap-" + Guid.NewGuid().ToString("N"));
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

    private IServiceProvider BuildServices(Action<IServiceCollection>? configure = null)
    {
        var services = CliTestHelper.CreateServiceProvider(
            workspace: _workspace,
            useRealFileStore: true,
            configure: configure);
        ConfigureInstaller((TestInstaller)services.GetRequiredService<ISkillInstaller>());
        return services;
    }

    [Fact]
    public async Task Remove_With_No_Skills()
    {
        // Arrange
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "--yes");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove --yes

            No skills found to remove.
            """);
    }

    [Fact]
    public async Task Remove_Named_Skill()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        projectLock.OnRemoveEntry = (_, _) => true;

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha", "--yes");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove alpha --yes

            Removing skills...
            Successfully removed 1 skill(s)
            """);
    }

    [Fact]
    public async Task Remove_Named_Skill_No_Match()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "nope", "--yes");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove nope --yes

            No matching skills found for: nope
            """);
    }

    [Fact]
    public async Task Remove_All()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        projectLock.OnRemoveEntry = (_, _) => true;

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "--all");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove --all

            Removing skills...
            Successfully removed 1 skill(s)
            """);
    }

    [Fact]
    public async Task Remove_With_Invalid_Agent_Fails()
    {
        // Arrange
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha", "--agent", "bogus");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove alpha --agent bogus
            # exit 1

            Invalid agents: bogus
            """);
    }

    [Fact]
    public async Task Remove_No_Names_Non_Interactive_Reports_Nothing_Specified()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "--yes");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove --yes

            No skills specified for removal.
            """);
    }

    [Fact]
    public async Task Remove_Interactive_Decline_Confirmation_Cancels()
    {
        // Arrange: a named skill arg skips the multiselect, so the only prompt is the removal
        // confirmation. Drive the real ConfirmationPrompt over an interactive console and decline ("n").
        // This drives a live prompt, so it asserts on behavior rather than a clean snapshot (the prompt's
        // own rendering would otherwise pollute the captured output).
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");

        var console = InteractiveConsole.Create();
        console.Input.PushTextWithEnter("n");
        var services = BuildServices(s => s.AddSingleton<IAnsiConsole>(console));

        // Act
        var cmd = services.GetRequiredService<RemoveCommand>();
        var exitCode = await cmd.Parse(["alpha"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: declining cancels with the cancellation exit code, nothing is removed, and the user is told.
        Assert.Equal(ExitCodeConstants.Cancelled, exitCode);
        Assert.True(Directory.Exists(Path.Combine(canonical, "alpha")));
        Assert.Contains("Removal cancelled", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_Reports_Failure_When_Lock_Update_Throws()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        projectLock.OnRemoveEntry = (_, _) => throw new InvalidOperationException("lock file is read-only");

        // Act
        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha", "--yes");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz remove alpha --yes
            # exit 1

            Removing skills...
            Failed to remove 1 skill(s)
              alpha: lock file is read-only
            """);
    }
}
