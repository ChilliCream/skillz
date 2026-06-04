using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Skills;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

public class AddCommandPrompterTests
{
    private static (
        AddCommandPrompter Prompter,
        TestInteractionService Interaction,
        TestGlobalLockFile GlobalLock) CreatePrompter()
    {
        var services = CliTestHelper.CreateServiceProvider();
        var interaction = services.GetRequiredService<TestInteractionService>();
        var registry = services.GetRequiredService<AgentRegistry>();
        var globalLock = services.GetRequiredService<TestGlobalLockFile>();
        var prompter = new AddCommandPrompter(interaction, registry, globalLock);
        return (prompter, interaction, globalLock);
    }

    private static ResolvedSkill Skill(string installName, string? pluginName)
        => new(
            Name: installName,
            Description: $"{installName} description",
            Content: string.Empty,
            InstallName: installName,
            SourceUrl: string.Empty,
            ProviderId: "test",
            SourceIdentifier: "test/repo",
            PluginName: pluginName);

    [Fact]
    public async Task SelectSkillsAsync_Should_GroupNamedFirstAndOtherLast_When_SomeSkillsAreUnclaimed()
    {
        // Arrange: a mix of plugin-claimed and unclaimed skills, deliberately out of order so the
        // ordering guarantees are actually exercised.
        var (prompter, interaction, _) = CreatePrompter();
        interaction.OnMultiSelectGroupedByIndex = (_, _) => [];
        ImmutableArray<ResolvedSkill> skills =
        [
            Skill("stack-init", "chillicream"),
            Skill("skill-author", pluginName: null),
            Skill("command", "chillicream"),
            Skill("dataloader", "chillicream")
        ];

        // Act
        await prompter.SelectSkillsAsync(skills, TestContext.Current.CancellationToken);

        // Assert: named group (title-cased) comes first, "Other" last; children are InstallName-ordered.
        Assert.NotNull(interaction.LastGroupedStructure);
        var structure = interaction.LastGroupedStructure;
        Assert.Equal(["Chillicream", "Other"], structure.Select(g => g.Group));
        Assert.Equal(
            [
                "command - command description",
                "dataloader - dataloader description",
                "stack-init - stack-init description"
            ],
            structure[0].Items);
        Assert.Equal(["skill-author - skill-author description"], structure[1].Items);
    }

    [Fact]
    public async Task SelectSkillsAsync_Should_OrderNamedGroupsByPluginNameOrdinal_When_MultiplePluginsPresent()
    {
        // Arrange: two named plugins given in reverse ordinal order, plus an unclaimed skill.
        var (prompter, interaction, _) = CreatePrompter();
        interaction.OnMultiSelectGroupedByIndex = (_, _) => [];
        ImmutableArray<ResolvedSkill> skills =
        [
            Skill("zeta-skill", "zebra"),
            Skill("apex-skill", "apple"),
            Skill("loose-skill", pluginName: null)
        ];

        // Act
        await prompter.SelectSkillsAsync(skills, TestContext.Current.CancellationToken);

        // Assert: named groups sorted by PluginName ordinal (Apple before Zebra), "Other" last.
        Assert.NotNull(interaction.LastGroupedStructure);
        Assert.Equal(["Apple", "Zebra", "Other"], interaction.LastGroupedStructure.Select(g => g.Group));
    }

    [Fact]
    public async Task SelectSkillsAsync_Should_SelectExactLeafAcrossGroups_When_UserPicksByIndex()
    {
        // Arrange: pick the second item of the named group (index 1 within group 0).
        var (prompter, interaction, _) = CreatePrompter();
        interaction.OnMultiSelectGroupedByIndex = (_, _) => [(0, 1)];
        ImmutableArray<ResolvedSkill> skills =
        [
            Skill("command", "chillicream"),
            Skill("dataloader", "chillicream"),
            Skill("skill-author", pluginName: null)
        ];

        // Act
        var selected = await prompter.SelectSkillsAsync(skills, TestContext.Current.CancellationToken);

        // Assert: exactly the second child of the named group (dataloader) is returned.
        Assert.Equal(["dataloader"], selected.Select(s => s.InstallName));
    }

    [Fact]
    public async Task SelectSkillsAsync_Should_UseFlatPath_When_NoSkillIsPluginClaimed()
    {
        // Arrange: every skill is unclaimed - a lone "Other" header would be noise, so the flat
        // picker should be used instead of the grouped tree.
        var (prompter, interaction, _) = CreatePrompter();
        interaction.OnMultiSelectByIndex = (_, _) => [1];
        ImmutableArray<ResolvedSkill> skills = [Skill("beta", pluginName: null), Skill("alpha", pluginName: null)];

        // Act
        var selected = await prompter.SelectSkillsAsync(skills, TestContext.Current.CancellationToken);

        // Assert: the grouped prompt was never invoked (no captured structure), and the flat picker
        // returned the second InstallName-ordered choice (beta, after alpha).
        Assert.Null(interaction.LastGroupedStructure);
        Assert.Equal(["beta"], selected.Select(s => s.InstallName));
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_PreSelectLastUsedAdditionalAgent_When_PreviouslySaved()
    {
        // Arrange: a prior run saved claude-code, the only additional (non-universal) agent here.
        // opencode and codex are universal (.agents/skills) and are always included, never
        // pre-selected as additional choices.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => ["claude-code"];
        interaction.OnSearchableSelectByIndex = (_, _, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: the saved last-used additional agent is handed to the prompt as a pre-selection.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["claude-code"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_DropUniversalAndUnavailableLastUsed_When_PreSelecting()
    {
        // Arrange: last-used includes a universal agent (always included, never pre-selected as an
        // additional choice) and an agent that is no longer available.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => ["claude-code", "opencode", "uninstalled-agent"];
        interaction.OnSearchableSelectByIndex = (_, _, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: only the still-available, additional last-used agent is pre-selected.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["claude-code"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_PreSelectAdditionalCommonDefaults_When_NoLastUsedSaved()
    {
        // Arrange: no prior selection saved. Of the common defaults {claude-code, opencode, codex}
        // only claude-code is additional (opencode and codex are universal).
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => null;
        interaction.OnSearchableSelectByIndex = (_, _, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex", "augment"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: falls back to the additional common defaults present in the available set.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["claude-code"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_SaveUniversalsPlusChosenAdditional_When_AgentsChosen()
    {
        // Arrange: user toggles the only additional agent (claude-code, selectable index 0).
        // The universals (codex, opencode) are always included.
        var (prompter, interaction, globalLock) = CreatePrompter();
        ImmutableArray<string>? saved = null;
        globalLock.OnGetLastSelectedAgents = () => null;
        globalLock.OnSaveLastSelectedAgents = agents => saved = [.. agents];
        interaction.OnSearchableSelectByIndex = (_, _, _) => [0];

        // Act
        var selected = await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: the result and the persisted set are the universals plus the chosen additional agent.
        Assert.Equal(
            ["claude-code", "codex", "opencode"],
            selected.OrderBy(a => a, StringComparer.Ordinal));
        Assert.NotNull(saved);
        Assert.Equal(
            ["claude-code", "codex", "opencode"],
            saved.Value.OrderBy(a => a, StringComparer.Ordinal));
    }
}
