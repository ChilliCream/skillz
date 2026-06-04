using Skillz.Interaction;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Interaction;

public class SearchableMultiSelectionPromptTests
{
    private static TestConsole CreateInteractiveConsole()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        return console;
    }

    private static SearchableSection<string> Always(params (string Label, string Value)[] items)
    {
        return new SearchableSection<string>("Universal", AlwaysIncluded: true, items);
    }

    private static SearchableSection<string> Selectable(params (string Label, string Value)[] items)
    {
        return new SearchableSection<string>("Additional", AlwaysIncluded: false, items);
    }

    [Fact]
    public async Task ShowAsync_Should_ReturnAlwaysIncludedPlusPreSelected_When_UserJustConfirms()
    {
        // Arrange: one always-included value plus one pre-selected selectable value; user only confirms.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Always(("Codex", "codex")), Selectable(("Claude Code", "claude-code"), ("Cursor", "cursor"))],
            preSelected: ["claude-code"]);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: always-included first (section order), then the pre-selected selectable value.
        Assert.Equal(["codex", "claude-code"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_FilterAndToggleHighlightedMatch_When_UserTypesQuery()
    {
        // Arrange: type "curs" to filter down to Cursor, toggle it on, confirm.
        using var console = CreateInteractiveConsole();
        console.Input.PushText("curs");
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Always(("Codex", "codex")), Selectable(("Claude Code", "claude-code"), ("Cursor", "cursor"))],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: always-included value plus the filtered-and-toggled selectable value.
        Assert.Equal(["codex", "cursor"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_KeepSelection_When_QueryFiltersItemOutOfView()
    {
        // Arrange: claude-code is pre-selected; the user types a query that hides it, then confirms.
        // Toggling state must persist even though the item is filtered out of the visible list.
        using var console = CreateInteractiveConsole();
        console.Input.PushText("cursor");
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Always(("Codex", "codex")), Selectable(("Claude Code", "claude-code"), ("Cursor", "cursor"))],
            preSelected: ["claude-code"]);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: claude-code survives the filter even though it was not on screen at confirm time.
        Assert.Equal(["codex", "claude-code"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_DisambiguateByIndex_When_LabelsAreIdentical()
    {
        // Arrange: two selectable items share the label "dup"; move to index 1 and toggle only it.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // cursor 0 -> 1
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the second "dup"
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Selectable(("dup", "first"), ("dup", "second"), ("other", "third"))],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: only the value at index 1 comes back; the identically-labelled sibling is untouched.
        Assert.Equal(["second"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_NarrowDifferently_When_BackspaceEditsQuery()
    {
        // Arrange: "claudx" matches nothing; backspace to "claud" matches Claude Code, toggle it on.
        using var console = CreateInteractiveConsole();
        console.Input.PushText("claudx");
        console.Input.PushKey(ConsoleKey.Backspace); // -> "claud"
        console.Input.PushKey(ConsoleKey.Spacebar); // highlighted match is Claude Code
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Selectable(("Claude Code", "claude-code"), ("Cursor", "cursor"))],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: backspacing revealed and toggled the match the over-typed query had hidden.
        Assert.Equal(["claude-code"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_ReturnAlwaysIncluded_When_NoSelectableItems()
    {
        // Arrange: only an always-included section; the user confirms.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Always(("Codex", "codex"), ("OpenCode", "opencode"))],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the always-included values are returned in section order.
        Assert.Equal(["codex", "opencode"], result);
    }
}
