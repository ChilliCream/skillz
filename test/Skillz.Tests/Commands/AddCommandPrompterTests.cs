using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

public class AddCommandPrompterTests
{
    private static (AddCommandPrompter Prompter, TestInteractionService Interaction, TestGlobalLockFile GlobalLock)
        CreatePrompter()
    {
        var services = CliTestHelper.CreateServiceProvider();
        var interaction = services.GetRequiredService<TestInteractionService>();
        var registry = services.GetRequiredService<AgentRegistry>();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        var prompter = new AddCommandPrompter(interaction, registry, globalLock);
        return (prompter, interaction, globalLock);
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_PreSelectLastUsedAgents_When_PreviouslySaved()
    {
        // Arrange: a prior run saved opencode as the chosen agent.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => ["opencode"];
        interaction.OnMultiSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: the saved last-used agent is handed to the prompt as a pre-selection.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["opencode"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_DropLastUsedAgentsNoLongerAvailable_When_PreSelecting()
    {
        // Arrange: last-used includes an agent that is not in the current available set.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => ["opencode", "uninstalled-agent"];
        interaction.OnMultiSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: only the still-available last-used agent is pre-selected.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["opencode"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_PreSelectCommonDefaults_When_NoLastUsedSaved()
    {
        // Arrange: no prior selection saved.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => null;
        interaction.OnMultiSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex", "augment"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: falls back to the common defaults that are present in the available set.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(
            ["claude-code", "codex", "opencode"],
            interaction.LastPreSelected.Cast<string>().OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_SaveSelection_When_AgentsChosen()
    {
        // Arrange: user picks the second offered agent (index 1).
        var (prompter, interaction, globalLock) = CreatePrompter();
        ImmutableArray<string>? saved = null;
        globalLock.OnGetLastSelectedAgents = () => null;
        globalLock.OnSaveLastSelectedAgents = agents => saved = [.. agents];
        interaction.OnMultiSelectByIndex = (_, _) => [1];

        // Act
        var selected = await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: the chosen agent is returned and persisted for next time.
        Assert.Equal(["opencode"], selected);
        Assert.NotNull(saved);
        Assert.Equal(["opencode"], saved.Value);
    }
}
