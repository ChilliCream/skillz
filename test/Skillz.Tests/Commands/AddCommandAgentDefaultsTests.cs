using System.Collections.Immutable;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Skillz;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Spectre.Console;
using Xunit;

namespace Skillz.Tests.Commands;

// The add command's interactive agent pre-selection and last-used persistence. Driven end-to-end
// through the command (a bare "./local-path" reaches the interactive agent path), since the logic
// now lives on AddCommand itself rather than a separate executor.
[Collection(CommandTestCollection.Name)]
public class AddCommandAgentDefaultsTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public AddCommandAgentDefaultsTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-add-exec-" + Guid.NewGuid().ToString("N"));
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

    private IServiceProvider BuildServices(
        Action<TestGlobalLockFile>? configureGlobalLock = null,
        Action<TestAgentSelector>? configureAgentSelector = null)
    {
        // These tests exercise the interactive agent-defaults path, so the install then reaches the
        // scope/mode/confirm prompts. Drive them over an interactive console: pick the first scope and
        // mode option, then accept the confirmation. (When fewer prompts fire, the surplus Enter is
        // read as the confirmation's default-accept, which is also a proceed.)
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.Enter); // install scope -> first option
        console.Input.PushKey(ConsoleKey.Enter); // install mode -> first option (when prompted)
        console.Input.PushTextWithEnter("y"); // confirmation

        var services = CliTestHelper.CreateServiceProvider(
            configure: s => s.AddSingleton<IAnsiConsole>(console));
        services.GetRequiredService<FakeFileStore>().CreateDirectory(_workspace);

        services.GetRequiredService<TestSourceParser>().OnParse =
            _ => new SkillSource.Local(_workspace, _workspace);
        services.GetRequiredService<TestSkillDiscovery>().OnDiscover =
            (_, _, _) => new[] { CreateSkill("alpha") };
        services.GetRequiredService<TestInstaller>().OnInstallRemoteSkill =
            (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}");

        configureGlobalLock?.Invoke(services.GetRequiredService<TestGlobalLockFile>());
        configureAgentSelector?.Invoke(services.GetRequiredService<TestAgentSelector>());

        return services;
    }

    private static Skill CreateSkill(string name) =>
        new(
            Name: name,
            Description: "test skill",
            Path: $"/tmp/{name}",
            RawContent: $"---\nname: {name}\ndescription: test skill\n---\n");

    // A bare interactive add (no -y, no --agent) reaching the agent picker.
    private static async Task<int> RunInteractiveAddAsync(IServiceProvider services)
    {
        var command = services.GetRequiredService<AddCommand>();
        return await command.Parse(["./local-path"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_PassLastUsedAsAgentDefaults_When_LastSelectionAvailable()
    {
        // Arrange: a prior run saved claude-code and opencode; both are still valid agents.
        var services = BuildServices(
            configureGlobalLock: l => l.OnGetLastSelectedAgents = () => ["claude-code", "opencode"]);

        // Act
        await RunInteractiveAddAsync(services);

        // Assert: the command computes the agent selector's pre-selection from last-used persistence.
        var agentSelector = services.GetRequiredService<TestAgentSelector>();
        Assert.NotNull(agentSelector.LastDefaults);
        Assert.Equal(["claude-code", "opencode"], agentSelector.LastDefaults);
    }

    [Fact]
    public async Task Should_DropUnavailableLastUsedAgents_When_ComputingDefaults()
    {
        // Arrange: last-used names an agent that is not a valid agent type; it must be dropped.
        var services = BuildServices(
            configureGlobalLock: l =>
                l.OnGetLastSelectedAgents = () => ["claude-code", "not-a-real-agent", "opencode"]);

        // Act
        await RunInteractiveAddAsync(services);

        // Assert: only the still-valid last-used agents reach the selector as defaults.
        var agentSelector = services.GetRequiredService<TestAgentSelector>();
        Assert.NotNull(agentSelector.LastDefaults);
        Assert.Equal(["claude-code", "opencode"], agentSelector.LastDefaults);
    }

    [Fact]
    public async Task Should_FallBackToUniversalsAndCommonDefaults_When_NoLastUsedSaved()
    {
        // Arrange: no prior selection saved, so the fallback is universals plus the common defaults
        // (claude-code, opencode, codex) that are present in the registry.
        var services = BuildServices(
            configureGlobalLock: l => l.OnGetLastSelectedAgents = () => null);

        var registry = services.GetRequiredService<AgentRegistry>();

        // Act
        await RunInteractiveAddAsync(services);

        // Assert: the fallback contains the common defaults and every universal, and excludes a
        // non-universal agent that is not a common default.
        var agentSelector = services.GetRequiredService<TestAgentSelector>();
        Assert.NotNull(agentSelector.LastDefaults);
        Assert.Contains("claude-code", agentSelector.LastDefaults);
        Assert.Contains("opencode", agentSelector.LastDefaults);
        Assert.Contains("codex", agentSelector.LastDefaults);
        foreach (var universal in registry.UniversalAgents)
        {
            Assert.Contains(universal, agentSelector.LastDefaults);
        }
        Assert.DoesNotContain("augment", agentSelector.LastDefaults);
    }

    [Fact]
    public async Task Should_PersistSelectedAgents_When_SelectionIsNonEmpty()
    {
        // Arrange: the selector returns a concrete selection; the command persists it as last-used.
        ImmutableArray<string>? saved = null;
        var services = BuildServices(
            configureGlobalLock: l => l.OnSaveLastSelectedAgents = agents => saved = [.. agents],
            configureAgentSelector: s => s.OnSelectAgents = (_, _) => ["claude-code", "opencode"]);

        // Act
        await RunInteractiveAddAsync(services);

        // Assert: the chosen agents are written back to the global lock for next time.
        Assert.NotNull(saved);
        Assert.Equal(["claude-code", "opencode"], saved.Value);
    }

    [Fact]
    public async Task Should_NotPersistAgents_When_SelectionIsEmpty()
    {
        // Arrange: the user selects nothing, so there is nothing worth remembering and the install
        // aborts for want of a target agent.
        var saved = false;
        var services = BuildServices(
            configureGlobalLock: l => l.OnSaveLastSelectedAgents = _ => saved = true,
            configureAgentSelector: s => s.OnSelectAgents = (_, _) => []);

        // Act: the empty selection makes the command fail (no agents selected) ...
        var exitCode = await RunInteractiveAddAsync(services);

        // Assert: ... and an empty selection is never persisted.
        Assert.Equal(ExitCodeConstants.Failure, exitCode);
        Assert.False(saved);
    }
}
