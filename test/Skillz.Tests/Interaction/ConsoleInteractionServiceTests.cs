using System.Collections.Immutable;
using Skillz.Interaction;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Interaction;

public class ConsoleInteractionServiceTests
{
    private static TestConsole CreateInteractiveConsole()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        return console;
    }

    [Fact]
    public async Task SelectAsync_Should_Return_Second_Value_When_Labels_Are_Identical()
    {
        // Arrange: two choices share the exact same label; only the value distinguishes them.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // move highlight from index 0 to index 1
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        var choices = new (string Label, string Value)[] { ("duplicate", "first"), ("duplicate", "second") };

        // Act
        var selected = await service.SelectAsync("Pick one", choices, TestContext.Current.CancellationToken);

        // Assert: the second item is picked by position, not by matching the (ambiguous) label.
        Assert.Equal("second", selected);
    }

    [Fact]
    public async Task MultiSelectAsync_Should_Select_Only_Intended_Item_When_Labels_Are_Identical()
    {
        // Arrange: index 0 and index 1 share a label; we want exactly the second one.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // move to index 1 (second "duplicate")
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle it on
        console.Input.PushKey(ConsoleKey.Enter); // confirm
        var service = new ConsoleInteractionService(console);

        var choices = new (string Label, string Value)[]
        {
            ("duplicate", "first"),
            ("duplicate", "second"),
            ("other", "third")
        };

        // Act
        var selected = await service.MultiSelectAsync("Pick some", choices, TestContext.Current.CancellationToken);

        // Assert: only the intended value comes back; the identically-labelled sibling is untouched.
        Assert.Equal(["second"], selected);
    }

    [Fact]
    public async Task MultiSelectAsync_Should_Return_PreSelected_Value_When_User_Just_Confirms()
    {
        // Arrange: pre-check "beta"; user presses Enter without toggling anything.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        var choices = new (string Label, string Value)[] { ("alpha", "alpha"), ("beta", "beta"), ("gamma", "gamma") };

        // Act
        var selected = await service.MultiSelectAsync(
            "Pick some",
            choices,
            ["beta"],
            TestContext.Current.CancellationToken);

        // Assert: the pre-checked item is returned even though the user never toggled it.
        Assert.Equal(["beta"], selected);
    }

    [Fact]
    public async Task MultiSelectAsync_Should_Add_To_PreSelection_When_User_Toggles_Another()
    {
        // Arrange: "alpha" is pre-checked; user moves to "gamma" and toggles it on too.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // index 1
        console.Input.PushKey(ConsoleKey.DownArrow); // index 2 (gamma)
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle gamma on
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        var choices = new (string Label, string Value)[] { ("alpha", "alpha"), ("beta", "beta"), ("gamma", "gamma") };

        // Act
        var selected = await service.MultiSelectAsync(
            "Pick some",
            choices,
            ["alpha"],
            TestContext.Current.CancellationToken);

        // Assert: both the pre-checked and the toggled value come back, in presentation order.
        Assert.Equal(["alpha", "gamma"], selected);
    }

    [Fact]
    public async Task MultiSelectAsync_Should_Return_Empty_When_No_Choices()
    {
        using var console = CreateInteractiveConsole();
        var service = new ConsoleInteractionService(console);

        var selected = await service.MultiSelectAsync(
            "Pick some",
            Array.Empty<(string, string)>(),
            TestContext.Current.CancellationToken);

        Assert.Empty(selected);
    }

    // The grouped prompt renders in SelectionMode.Leaf, where the navigable rows are, in order:
    // [Group A header, a-dup, a-two, Group B header, b-dup, b-two]. The tests below drive that
    // layout. Empirically (Spectre 0.50.0) Leaf mode returns only leaf handles and a header toggle
    // cascades to its children - both relied on here.
    private sealed record Item(string Group, string Label, string Value);

    private static Item[] CollidingGroups()
        => [
            new("Group A", "dup", "a-dup"),
            new("Group A", "a-two", "a-two"),
            new("Group B", "dup", "b-dup"),
            new("Group B", "b-two", "b-two")
        ];

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Return_Only_Intended_Leaf_When_Labels_Collide_Across_Groups()
    {
        // Arrange: "dup" appears in both groups; we want exactly the one under Group B.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // a-dup
        console.Input.PushKey(ConsoleKey.DownArrow); // a-two
        console.Input.PushKey(ConsoleKey.DownArrow); // Group B header
        console.Input.PushKey(ConsoleKey.DownArrow); // b-dup
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the Group B "dup"
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        // Act
        var selected = await service.MultiSelectGroupedAsync(
            "Pick some",
            CollidingGroups(),
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        // Assert: only Group B's "dup" comes back; the identically-labelled Group A sibling is untouched.
        Assert.Equal(["b-dup"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Select_All_Children_When_Group_Header_Toggled()
    {
        // Arrange: highlight Group A's header (the top row) and toggle it.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle Group A header -> cascades to its children
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        // Act
        var selected = await service.MultiSelectGroupedAsync(
            "Pick some",
            CollidingGroups(),
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        // Assert: every child of Group A is selected; Group B is untouched. The header itself never
        // appears in the result.
        Assert.Equal(["a-dup", "a-two"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Render_Without_Crashing_When_Labels_Contain_Markup()
    {
        // Arrange: a '[' in a header or leaf label would crash Spectre's markup parser at render time
        // if the converter output were not escaped - a regression guard for the grouped picker.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the header -> cascades to its one child
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        Item[] bracketed = [new("Group [A]", "array[]-helpers", "v")];

        // Act
        var selected = await service.MultiSelectGroupedAsync(
            "Pick some",
            bracketed,
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        // Assert: it renders and returns the leaf instead of throwing "Unbalanced markup stack".
        Assert.Equal(["v"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Return_Empty_When_No_Choices()
    {
        using var console = CreateInteractiveConsole();
        var service = new ConsoleInteractionService(console);

        var selected = await service.MultiSelectGroupedAsync(
            "Pick some",
            Array.Empty<Item>(),
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        Assert.Empty(selected);
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Navigate_With_Vim_Keys_When_J_And_K_Pressed()
    {
        // Arrange: drive navigation with j (down) and k (up) instead of the arrow keys - 'j' walks all
        // the way down to b-two, then 'k' steps back up to b-dup, which we toggle and confirm.
        using var console = CreateInteractiveConsole();
        console.Input.PushCharacter('j'); // a-dup
        console.Input.PushCharacter('j'); // a-two
        console.Input.PushCharacter('j'); // Group B header
        console.Input.PushCharacter('j'); // b-dup
        console.Input.PushCharacter('j'); // b-two
        console.Input.PushCharacter('k'); // back up to b-dup
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        // Act
        var selected = await service.MultiSelectGroupedAsync(
            "Pick some",
            CollidingGroups(),
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        // Assert: j/k moved the cursor exactly like Down/Up, landing on and selecting Group B's "dup".
        Assert.Equal(["b-dup"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Show_Space_To_Select_Hint_In_Output()
    {
        // A choice is required, so the prompt must tell the user to select with space before enter.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // satisfy the required selection so the prompt completes
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new ConsoleInteractionService(console);

        // Act
        await service.MultiSelectGroupedAsync(
            "Pick some",
            CollidingGroups(),
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        // Assert: the rendered prompt spells out the space-to-select instruction.
        Assert.Contains("space", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Nudge_And_Reprompt_When_Enter_Pressed_With_No_Selection()
    {
        // Arrange: user presses enter with nothing selected (should be nudged), then selects on the
        // re-shown prompt and confirms.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Enter); // nothing selected -> highlighted nudge + re-show
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle Group A header -> cascades to its children
        console.Input.PushKey(ConsoleKey.Enter); // confirm
        var service = new ConsoleInteractionService(console);

        // Act
        var selected = await service.MultiSelectGroupedAsync(
            "Pick some",
            CollidingGroups(),
            i => i.Group,
            i => i.Label,
            TestContext.Current.CancellationToken);

        // Assert: the empty enter produced the highlighted nudge, and the retry returned the selection.
        Assert.Equal(["a-dup", "a-two"], selected.Select(i => i.Value));
        Assert.Contains("at least one", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultiSelectGroupedAsync_Should_Throw_OperationCanceled_When_Token_Already_Cancelled()
    {
        // The re-prompt loop is bounded by the token, not while(true): a cancelled token must break out
        // rather than spin.
        using var console = CreateInteractiveConsole();
        var service = new ConsoleInteractionService(console);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MultiSelectGroupedAsync("Pick some", CollidingGroups(), i => i.Group, i => i.Label, cts.Token)
        );
    }

    [Fact]
    public async Task SearchableMultiSelectAsync_Should_ReturnAlwaysIncludedPlusSelectablePreSelected_When_NonInteractive()
    {
        // Arrange: a non-interactive console cannot drive the key loop, so the service must fall back
        // to a deterministic selection without prompting.
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = false;
        var service = new ConsoleInteractionService(console);

        var sections = new[]
        {
            new SearchableSection<string>(
                "Universal",
                AlwaysIncluded: true,
                [("Codex", "codex"), ("OpenCode", "opencode")]),
            new SearchableSection<string>(
                "Additional",
                AlwaysIncluded: false,
                [("Claude Code", "claude-code"), ("Cursor", "cursor")])
        };

        // "claude-code" IS selectable; "ghost" is in no section and must be dropped.
        var preSelected = new[] { "claude-code", "ghost" };

        // Act
        var selected = await service.SearchableMultiSelectAsync(
            "Pick agents",
            sections,
            preSelected,
            TestContext.Current.CancellationToken);

        // Assert: all always-included values (section order) plus only the selectable pre-selected
        // value; the non-selectable "ghost" is filtered out.
        Assert.Equal(["codex", "opencode", "claude-code"], selected);
    }
}
