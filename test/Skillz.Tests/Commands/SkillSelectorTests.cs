using System.Collections.Immutable;
using Skillz.Commands.Selection;
using Skillz.Skills;
using Skillz.Tests.TestServices;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Commands;

public class SkillSelectorTests
{
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
    public async Task SelectAsync_Should_OrderChildrenByInstallName_And_PlaceOtherLast()
    {
        // A mix of plugin-claimed and unclaimed skills, deliberately out of order. The named group is
        // presented first with its children InstallName-ordered, the unclaimed "Other" bucket last.
        // Leaf rows in presentation order: [Chillicream, command, dataloader, stack-init, Other,
        // skill-author]. Toggle every leaf; the grouped prompt returns leaves in presentation order, so
        // the result order is the full structure.
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.DownArrow);  // command
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);  // dataloader
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);  // stack-init
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);  // Other header
        console.Input.PushKey(ConsoleKey.DownArrow);  // skill-author
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var selector = new SkillSelector(console);

        ImmutableArray<ResolvedSkill> skills =
        [
            Skill("stack-init", "chillicream"),
            Skill("skill-author", pluginName: null),
            Skill("command", "chillicream"),
            Skill("dataloader", "chillicream")
        ];

        var selected = await selector.SelectAsync(skills, TestContext.Current.CancellationToken);

        // Named group's children alphabetical, then the unclaimed skill last.
        Assert.Equal(
            ["command", "dataloader", "stack-init", "skill-author"],
            selected.Select(s => s.InstallName));
        // Headers render title-cased, with the unclaimed bucket labelled "Other".
        Assert.Contains("Chillicream", console.Output, StringComparison.Ordinal);
        Assert.Contains("Other", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectAsync_Should_OrderNamedGroupsByPluginNameOrdinal()
    {
        // Two named plugins given in reverse ordinal order, plus an unclaimed skill. Leaf rows in
        // presentation order: [Apple, apex-skill, Zebra, zeta-skill, Other, loose-skill]. Toggle every
        // leaf; the returned order proves named groups sort by PluginName ordinal (Apple before Zebra),
        // with "Other" last.
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.DownArrow);  // apex-skill
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);  // Zebra header
        console.Input.PushKey(ConsoleKey.DownArrow);  // zeta-skill
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);  // Other header
        console.Input.PushKey(ConsoleKey.DownArrow);  // loose-skill
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var selector = new SkillSelector(console);

        ImmutableArray<ResolvedSkill> skills =
        [
            Skill("zeta-skill", "zebra"),
            Skill("apex-skill", "apple"),
            Skill("loose-skill", pluginName: null)
        ];

        var selected = await selector.SelectAsync(skills, TestContext.Current.CancellationToken);

        Assert.Equal(["apex-skill", "zeta-skill", "loose-skill"], selected.Select(s => s.InstallName));
        Assert.Contains("Apple", console.Output, StringComparison.Ordinal);
        Assert.Contains("Zebra", console.Output, StringComparison.Ordinal);
        Assert.Contains("Other", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectAsync_Should_SelectExactLeafAcrossGroups_When_UserPicksByIndex()
    {
        // Grouped leaf rows in presentation order: [Chillicream header, command, dataloader, Other
        // header, skill-author]. Navigate to the second child of the named group (dataloader) and toggle it.
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.DownArrow); // command
        console.Input.PushKey(ConsoleKey.DownArrow); // dataloader
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var selector = new SkillSelector(console);

        ImmutableArray<ResolvedSkill> skills =
        [
            Skill("command", "chillicream"),
            Skill("dataloader", "chillicream"),
            Skill("skill-author", pluginName: null)
        ];

        var selected = await selector.SelectAsync(skills, TestContext.Current.CancellationToken);

        // Exactly the second child of the named group (dataloader) is returned.
        Assert.Equal(["dataloader"], selected.Select(s => s.InstallName));
    }

    [Fact]
    public async Task SelectAsync_Should_UseFlatPath_When_NoSkillIsPluginClaimed()
    {
        // Every skill is unclaimed - a lone "Other" header would be noise, so the FLAT picker is used.
        // The flat list has no header row, so a single Down lands on the second item (beta); had the
        // grouped tree been used, that same Down would have landed on the first item under "Other".
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.DownArrow); // alpha -> beta (no header row to skip)
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var selector = new SkillSelector(console);

        ImmutableArray<ResolvedSkill> skills = [Skill("beta", pluginName: null), Skill("alpha", pluginName: null)];

        var selected = await selector.SelectAsync(skills, TestContext.Current.CancellationToken);

        // The flat picker returned the second InstallName-ordered choice (beta, after alpha).
        Assert.Equal(["beta"], selected.Select(s => s.InstallName));
    }

    [Fact]
    public async Task SelectAsync_Should_DegradeToEmpty_When_ConsoleNonInteractive()
    {
        // A console that cannot drive the key loop (e.g. output is redirected) must not crash the
        // Spectre prompt: WithDefault degrades to an empty selection so the caller cancels gracefully.
        // Cover both the grouped picker (plugin-claimed) and the flat picker (all unclaimed).
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = false;
        var selector = new SkillSelector(console);

        ImmutableArray<ResolvedSkill> grouped = [Skill("alpha", "plugin"), Skill("beta", "plugin")];
        ImmutableArray<ResolvedSkill> flat = [Skill("alpha", pluginName: null), Skill("beta", pluginName: null)];

        Assert.Empty(await selector.SelectAsync(grouped, TestContext.Current.CancellationToken));
        Assert.Empty(await selector.SelectAsync(flat, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SelectAsync_Should_ReturnWithoutPrompting_When_AtMostOneSkill()
    {
        // Zero or one skill never reaches the picker: nothing to choose, or nothing to choose from.
        var selector = new SkillSelector(InteractiveConsole.Create());
        var one = Skill("solo", pluginName: null);

        Assert.Empty(await selector.SelectAsync([], TestContext.Current.CancellationToken));
        Assert.Equal(["solo"], (await selector.SelectAsync([one], TestContext.Current.CancellationToken))
            .Select(s => s.InstallName));
    }
}
