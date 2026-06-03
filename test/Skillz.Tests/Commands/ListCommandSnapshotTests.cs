using CookieCrumble;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Install;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class ListCommandSnapshotTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public ListCommandSnapshotTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-list-snap-" + Guid.NewGuid().ToString("N"));
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

    private IServiceProvider BuildServices()
    {
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var installer = (TestInstaller)services.GetRequiredService<ISkillInstaller>();
        installer.OnGetCanonicalSkillsDir = (_, cwd) => Path.Combine(cwd ?? _workspace, ".agents", "skills");
        installer.OnGetAgentBaseDir = (agentType, _, cwd) => agentType == "claude-code"
            ? Path.Combine(cwd ?? _workspace, ".claude", "skills")
            : Path.Combine(cwd ?? _workspace, ".agents", "skills");
        return services;
    }

    private static void CreateSkill(string baseDir, string name)
    {
        var dir = Path.Combine(baseDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: {name}\ndescription: a test\n---\n");
    }

    [Fact]
    public async Task List_With_No_Skills()
    {
        // Arrange
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "list");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz list

            No project skills found.
            Try listing global skills with -g
            """);
    }

    [Fact]
    public async Task List_With_Installed_Skill()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "list");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz list

            Project Skills

            Skill  Path                    Agents
            alpha  ./.agents/skills/alpha  not linked
            """);
    }

    [Fact]
    public async Task List_As_Json()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".agents", "skills");
        Directory.CreateDirectory(canonical);
        CreateSkill(canonical, "alpha");
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "list", "--json");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz list --json

            [
              {
                "name": "alpha",
                "path": "<cwd>/.agents/skills/alpha",
                "scope": "project",
                "agents": []
              }
            ]
            """);
    }

    [Fact]
    public async Task List_With_Invalid_Agent_Fails()
    {
        // Arrange
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "list", "--agent", "bogus");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz list --agent bogus
            # exit 1

            Invalid agents: bogus
            """);
    }

    [Fact]
    public async Task List_Global_With_No_Skills()
    {
        // Arrange
        var services = BuildServices();

        // Act
        var output = await CommandSnapshot.RunAsync(services, "list", "--global");

        // Assert
        output.MatchInlineSnapshot(
            """
            $ skillz list --global

            No global skills found.
            """);
    }
}
