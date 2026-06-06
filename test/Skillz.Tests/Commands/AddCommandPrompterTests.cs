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
    public async Task SelectAgentsAsync_Should_InterleaveUniversalsAlphabetically_When_Prompting()
    {
        // Arrange: a deliberately shuffled mix where universals and non-universals must INTERLEAVE by
        // display name, not group on top. cursor (Cursor) and warp (Warp) are universal (.agents/skills);
        // claude-code (Claude Code) and goose (Goose) are not. Sorted by DisplayName the order is
        // Claude Code, Cursor, Goose, Warp - i.e. non, universal, non, universal: the first row is a
        // non-universal and the LAST row is a universal, which is impossible if universals were grouped first.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => null;
        interaction.OnSearchableSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["warp", "claude-code", "goose", "cursor"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: one alphabetical list with universals in their natural position - mandatory + noted where
        // universal, plain otherwise - proving the list is merged, not "universals first".
        Assert.NotNull(interaction.LastSearchableItems);
        Assert.Equal(
            [
                ("Claude Code (claude-code)", false, null),
                ("Cursor (cursor)", true, "universal · .agents"),
                ("Goose (goose)", false, null),
                ("Warp (warp)", true, "universal · .agents")
            ],
            interaction.LastSearchableItems);
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_PreSelectLastUsedAvailableAgents_When_PreviouslySaved()
    {
        // Arrange: a prior run saved claude-code and opencode; both are still available.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => ["claude-code", "opencode"];
        interaction.OnSearchableSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: the saved last-used agents (still available) are handed to the prompt as pre-selection.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["claude-code", "opencode"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_DropUnavailableLastUsed_When_PreSelecting()
    {
        // Arrange: last-used includes an agent that is no longer available; it must be dropped.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => ["claude-code", "opencode", "uninstalled-agent"];
        interaction.OnSearchableSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: only the still-available last-used agents are pre-selected.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(["claude-code", "opencode"], interaction.LastPreSelected.Cast<string>());
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_PreSelectUniversalsPlusCommonDefaults_When_NoLastUsedSaved()
    {
        // Arrange: no prior selection saved. The fallback pre-selection is all universals (opencode,
        // codex) plus whichever of {claude-code, opencode, codex} are available - here claude-code.
        var (prompter, interaction, globalLock) = CreatePrompter();
        globalLock.OnGetLastSelectedAgents = () => null;
        interaction.OnSearchableSelectByIndex = (_, _) => [];

        // Act
        await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex", "augment"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: universals plus the common-default claude-code are pre-selected; augment is not.
        Assert.NotNull(interaction.LastPreSelected);
        Assert.Equal(
            ["claude-code", "codex", "opencode"],
            interaction.LastPreSelected.Cast<string>().OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public async Task SelectAgentsAsync_Should_SaveChosenAgentsPlusMandatoryUniversals_When_AgentsChosen()
    {
        // Arrange: the merged list is DisplayName-ordered: Claude Code (claude-code, non-universal, index
        // 0), Codex (codex, mandatory universal, index 1), OpenCode (opencode, mandatory universal, index 2).
        // The user toggles only the non-universal claude-code at index 0; the mandatory universals are always
        // part of the result regardless.
        var (prompter, interaction, globalLock) = CreatePrompter();
        ImmutableArray<string>? saved = null;
        globalLock.OnGetLastSelectedAgents = () => null;
        globalLock.OnSaveLastSelectedAgents = agents => saved = [.. agents];
        interaction.OnSearchableSelectByIndex = (_, _) => [0];

        // Act
        var selected = await prompter.SelectAgentsAsync(
            ["claude-code", "opencode", "codex"],
            global: false,
            TestContext.Current.CancellationToken);

        // Assert: the result and the persisted set are the toggled agent plus the mandatory universals,
        // in item (DisplayName) order.
        Assert.Equal(["claude-code", "codex", "opencode"], selected);
        Assert.NotNull(saved);
        Assert.Equal(["claude-code", "codex", "opencode"], saved.Value);
    }
}
