using CookieCrumble;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Install;
using Skillz.Locking;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
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

    private IServiceProvider BuildServices()
    {
        var services = CliTestHelper.CreateServiceProvider(useRealFileStore: true);
        ConfigureInstaller((TestInstaller)services.GetRequiredService<ISkillInstaller>());
        return services;
    }

    [Fact]
    public async Task Remove_With_No_Skills()
    {
        var services = BuildServices();

        var output = await CommandSnapshot.RunAsync(services, "remove", "--yes");

        output.MatchInlineSnapshot(
            """
            $ skillz remove --yes

            No skills found to remove.
            """);
    }

    [Fact]
    public async Task Remove_Named_Skill()
    {
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        projectLock.OnRemoveEntry = (_, _) => true;

        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha", "--yes");

        output.MatchInlineSnapshot(
            """
            $ skillz remove alpha --yes

            Successfully removed 1 skill(s)
            """);
    }

    [Fact]
    public async Task Remove_Named_Skill_No_Match()
    {
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();

        var output = await CommandSnapshot.RunAsync(services, "remove", "nope", "--yes");

        output.MatchInlineSnapshot(
            """
            $ skillz remove nope --yes

            No matching skills found for: nope
            """);
    }

    [Fact]
    public async Task Remove_All()
    {
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        projectLock.OnRemoveEntry = (_, _) => true;

        var output = await CommandSnapshot.RunAsync(services, "remove", "--all");

        output.MatchInlineSnapshot(
            """
            $ skillz remove --all

            Successfully removed 1 skill(s)
            """);
    }

    [Fact]
    public async Task Remove_With_Invalid_Agent_Fails()
    {
        var services = BuildServices();

        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha", "--agent", "bogus");

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
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();

        var output = await CommandSnapshot.RunAsync(services, "remove", "--yes");

        output.MatchInlineSnapshot(
            """
            $ skillz remove --yes

            No skills specified for removal.
            """);
    }

    [Fact]
    public async Task Remove_Interactive_Decline_Confirmation_Cancels()
    {
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var prompter = services.GetRequiredService<TestRemoveCommandPrompter>();
        prompter.OnConfirmRemoval = _ => false;

        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha");

        output.MatchInlineSnapshot(
            """
            $ skillz remove alpha
            # exit 130

            Removal cancelled
            """);
    }

    [Fact]
    public async Task Remove_Reports_Failure_When_Lock_Update_Throws()
    {
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();
        var projectLock = (TestProjectLockFile)services.GetRequiredService<IProjectLockFile>();
        projectLock.OnRemoveEntry = (_, _) => throw new InvalidOperationException("lock file is read-only");

        var output = await CommandSnapshot.RunAsync(services, "remove", "alpha", "--yes");

        output.MatchInlineSnapshot(
            """
            $ skillz remove alpha --yes
            # exit 1

            Failed to remove 1 skill(s)
              alpha: lock file is read-only
            """);
    }
}
