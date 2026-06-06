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

    private static SearchableItem<string> Item(string value, string label)
        => new(value, label);

    [Fact]
    public async Task ShowAsync_Should_ReturnPreSelected_When_UserJustConfirms()
    {
        // Arrange: two items, one pre-selected; user only presses Enter.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Item("claude-code", "Claude Code"), Item("cursor", "Cursor")],
            preSelected: ["claude-code"]);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the pre-selected value comes back untouched.
        Assert.Equal(["claude-code"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_FilterAndToggleMatch_When_UserTypesQuery()
    {
        // Arrange: type "curs" to filter down to Cursor, toggle it on, confirm.
        using var console = CreateInteractiveConsole();
        console.Input.PushText("curs");
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Item("claude-code", "Claude Code"), Item("cursor", "Cursor")],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: only the filtered-and-toggled value comes back.
        Assert.Equal(["cursor"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_KeepSelection_When_QueryFiltersItemOutOfView()
    {
        // Arrange: claude-code is pre-selected; the user types a query that hides it, then confirms.
        // Selection state must persist even though the item is filtered out of the visible list.
        using var console = CreateInteractiveConsole();
        console.Input.PushText("cursor");
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Item("claude-code", "Claude Code"), Item("cursor", "Cursor")],
            preSelected: ["claude-code"]);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: claude-code survives the filter even though it was not on screen at confirm time.
        Assert.Equal(["claude-code"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_DisambiguateByIndex_When_LabelsAreIdentical()
    {
        // Arrange: two items share the label "dup"; move to index 1 and toggle only it.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // cursor 0 -> 1
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the second "dup"
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [Item("first", "dup"), Item("second", "dup"), Item("third", "other")],
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
            [Item("claude-code", "Claude Code"), Item("cursor", "Cursor")],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: backspacing revealed and toggled the match the over-typed query had hidden.
        Assert.Equal(["claude-code"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_MatchNote_When_UserTypesNoteText()
    {
        // Arrange: typing "universal" matches only the noted item via its note, not its label. The item
        // is non-mandatory so it can be toggled; toggle the single visible match off (it is pre-selected).
        using var console = CreateInteractiveConsole();
        console.Input.PushText("universal");
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the note-matched item off
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: false, Note: "universal · .agents"),
                Item("cursor", "Cursor")
            ],
            preSelected: ["amp"]);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the note query surfaced only "amp", and toggling it off leaves nothing selected.
        Assert.Empty(result);
    }

    [Fact]
    public async Task ShowAsync_Should_StartMandatoryItemSelected_When_NotInPreSelected()
    {
        // Arrange: a mandatory (universal) item is NOT in the caller's pre-selection, yet it must start
        // selected anyway. The user just confirms.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: true, Note: "universal · .agents"),
                Item("cursor", "Cursor")
            ],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the mandatory item comes back even though it was never pre-selected or toggled.
        Assert.Equal(["amp"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_KeepMandatoryItemSelected_When_SpacePressedOnIt()
    {
        // Arrange: the cursor rests on a mandatory item (index 0); pressing space must be a no-op so it
        // stays selected and the non-mandatory sibling is left alone.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // attempt to deselect the mandatory item -> no-op
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: true, Note: "universal · .agents"),
                Item("cursor", "Cursor")
            ],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: space could not deselect the mandatory item; it is still in the result.
        Assert.Equal(["amp"], result);
    }

    [Fact]
    public async Task ShowAsync_Should_StillReturnMandatoryItem_When_QueryFiltersItOutOfView()
    {
        // Arrange: a mandatory (universal) "amp" starts selected; the user types "cursor", which matches
        // neither its label nor its "universal · .agents" note, so "amp" is filtered off screen. A
        // mandatory item must survive the filter and still come back even when it was not visible at
        // confirm time - the highest-value guarantee for mandatory items.
        using var console = CreateInteractiveConsole();
        console.Input.PushText("cursor");
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: true, Note: "universal · .agents"),
                Item("cursor", "Cursor")
            ],
            preSelected: []);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the mandatory "amp" is returned despite being filtered out of the visible list.
        Assert.Contains("amp", result);
    }

    [Fact]
    public async Task ShowAsync_Should_RenderBlueMarkerButPlainLabel_When_ItemIsMandatory()
    {
        // Arrange: two mandatory items so the second is NOT the cursor row and thus carries no underline -
        // any color on its label would have to be the mandatory styling. Capture the raw ANSI frames.
        using var console = CreateInteractiveConsole();
        console.EmitAnsiSequences = true;
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: true, Note: "universal · .agents"),
                new SearchableItem<string>("zed", "Zed (zed)", Mandatory: true, Note: "universal · .agents")
            ],
            preSelected: []);

        // Act
        await prompt.ShowAsync(console, TestContext.Current.CancellationToken);
        var output = console.Output;

        // Assert: Spectre renders [blue] as the 256-color SGR "ESC[38;5;12m". The mandatory marker glyph ●
        // (U+25CF) is wrapped in that blue span...
        Assert.Contains("\u001b[38;5;12m\u25cf", output);

        // ...while the label text "Zed" (a non-cursor row, so no underline either) immediately follows a
        // reset, never the blue span - guarding against the legacy behavior that colored the whole label.
        Assert.Contains("Zed (zed)", output);
        Assert.DoesNotContain("\u001b[38;5;12mAmp", output);
        Assert.DoesNotContain("\u001b[38;5;12mZed", output);
    }

    [Fact]
    public async Task ShowAsync_Should_RenderLegendWithBlueAndGreenMarkers_When_Rendered()
    {
        // Arrange: a mandatory universal plus a pre-selected regular agent, so the frame shows the blue and
        // green markers the legend describes. Capture the raw ANSI frames.
        using var console = CreateInteractiveConsole();
        console.EmitAnsiSequences = true;
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: true, Note: "universal \u00b7 .agents"),
                Item("cursor", "Cursor")
            ],
            preSelected: ["cursor"]);

        // Act
        await prompt.ShowAsync(console, TestContext.Current.CancellationToken);
        var output = console.Output;

        // Assert: the legend is rendered at the bottom with all three states - the blue dot (SGR
        // ESC[38;5;12m) labelled "universal (always included)", the green dot (SGR ESC[38;5;2m) labelled
        // "selected", and the hollow dot labelled "not selected".
        Assert.Contains("\u001b[38;5;12m\u25cf\u001b[0m \u001b[2muniversal (always included)", output);
        Assert.Contains("\u001b[38;5;2m\u25cf\u001b[0m \u001b[2mselected", output);
        Assert.Contains("not selected", output);
    }

    [Fact]
    public async Task ShowAsync_Should_ExcludeMandatoryItemsFromSummary_When_Rendered()
    {
        // Arrange: a mandatory universal plus a selected regular agent. Capture the raw ANSI frames.
        using var console = CreateInteractiveConsole();
        console.EmitAnsiSequences = true;
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new SearchableMultiSelectionPrompt<string>(
            "Pick agents",
            [
                new SearchableItem<string>("amp", "Amp (amp)", Mandatory: true, Note: "universal · .agents"),
                Item("cursor", "Cursor (cursor)")
            ],
            preSelected: ["cursor"]);

        // Act
        await prompt.ShowAsync(console, TestContext.Current.CancellationToken);
        var output = console.Output;

        // Assert: the summary lists the regular pick, not the always-on universal.
        Assert.Contains("\u001b[2mSelected:\u001b[0m Cursor (cursor)", output);
        Assert.DoesNotContain("Amp (amp), Cursor (cursor)", output);
    }
}
