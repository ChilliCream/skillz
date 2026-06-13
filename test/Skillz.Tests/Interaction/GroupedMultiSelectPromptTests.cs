using Skillz.Interaction.Prompts;
using Skillz.Utils;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Interaction;

public class GroupedMultiSelectPromptTests
{
    private static TestConsole CreateInteractiveConsole()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        return console;
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

    private static GroupedMultiSelectPrompt<Item> Prompt(IEnumerable<Item> items)
        => new("Pick some", items, i => i.Group, i => i.Label);

    [Fact]
    public async Task ShowAsync_Should_Return_Only_Intended_Leaf_When_Labels_Collide_Across_Groups()
    {
        // Arrange: "dup" appears in both groups; we want exactly the one under Group B.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // a-dup
        console.Input.PushKey(ConsoleKey.DownArrow); // a-two
        console.Input.PushKey(ConsoleKey.DownArrow); // Group B header
        console.Input.PushKey(ConsoleKey.DownArrow); // b-dup
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the Group B "dup"
        console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var selected = await Prompt(CollidingGroups()).ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: only Group B's "dup" comes back; the identically-labelled Group A sibling is untouched.
        Assert.Equal(["b-dup"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task ShowAsync_Should_Select_All_Children_When_Group_Header_Toggled()
    {
        // Arrange: highlight Group A's header (the top row) and toggle it.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle Group A header -> cascades to its children
        console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var selected = await Prompt(CollidingGroups()).ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: every child of Group A is selected; Group B is untouched. The header itself never
        // appears in the result.
        Assert.Equal(["a-dup", "a-two"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task ShowAsync_Should_Render_Without_Crashing_When_Labels_Contain_Markup()
    {
        // Arrange: a '[' in a header or leaf label would crash Spectre's markup parser at render time
        // if the converter output were not escaped - a regression guard for the grouped picker.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the header -> cascades to its one child
        console.Input.PushKey(ConsoleKey.Enter);

        Item[] bracketed = [new("Group [A]", "array[]-helpers", "v")];

        // Act
        var selected = await Prompt(bracketed).ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: it renders and returns the leaf instead of throwing "Unbalanced markup stack".
        Assert.Equal(["v"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task ShowAsync_Should_Navigate_With_Vim_Keys_When_J_And_K_Pressed()
    {
        // Arrange: drive navigation with j (down) and k (up) instead of the arrow keys - 'j' walks all
        // the way down to b-two, then 'k' steps back up to b-dup, which we toggle and confirm. The
        // prompt runs over a VimNavConsole, exactly as the SkillSelector composes it.
        using var console = CreateInteractiveConsole();
        console.Input.PushCharacter('j'); // a-dup
        console.Input.PushCharacter('j'); // a-two
        console.Input.PushCharacter('j'); // Group B header
        console.Input.PushCharacter('j'); // b-dup
        console.Input.PushCharacter('j'); // b-two
        console.Input.PushCharacter('k'); // back up to b-dup
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var selected = await Prompt(CollidingGroups())
            .ShowAsync(new VimNavConsole(console), TestContext.Current.CancellationToken);

        // Assert: j/k moved the cursor exactly like Down/Up, landing on and selecting Group B's "dup".
        Assert.Equal(["b-dup"], selected.Select(i => i.Value));
    }

    [Fact]
    public async Task ShowAsync_Should_Show_Space_To_Select_Hint_In_Output()
    {
        // A choice is required, so the prompt must tell the user to select with space before enter.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // satisfy the required selection so the prompt completes
        console.Input.PushKey(ConsoleKey.Enter);

        // Act
        await Prompt(CollidingGroups()).ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the rendered prompt spells out the space-to-select instruction.
        Assert.Contains("space", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_Should_Throw_When_No_Choices()
    {
        // No leaves means a choiceless Spectre prompt, and an empty result wrapped in RequireNonEmpty
        // would re-show forever; building one with no choices is a programming error, so fail fast.
        Assert.Throws<InvalidOperationException>(() => Prompt(Array.Empty<Item>()));
    }
}
