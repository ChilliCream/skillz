using Skillz.Interaction.Prompts;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Interaction;

public class MultiSelectPromptTests
{
    private static TestConsole CreateInteractiveConsole()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        return console;
    }

    [Fact]
    public async Task ShowAsync_Should_Select_Only_Intended_Item_When_Labels_Are_Identical()
    {
        // Arrange: index 0 and index 1 share a label; we want exactly the second one.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // move to index 1 (second "duplicate")
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle it on
        console.Input.PushKey(ConsoleKey.Enter); // confirm

        var choices = new (string Label, string Value)[]
        {
            ("duplicate", "first"),
            ("duplicate", "second"),
            ("other", "third")
        };
        var prompt = new MultiSelectPrompt<string>("Pick some", choices);

        // Act
        var selected = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: only the intended value comes back; the identically-labelled sibling is untouched.
        Assert.Equal(["second"], selected);
    }

    [Fact]
    public void Constructor_Should_Throw_When_No_Choices()
    {
        // Spectre cannot show a choiceless prompt, and an empty result wrapped in RequireNonEmpty would
        // re-show forever; building one with no choices is a programming error, so fail fast.
        Assert.Throws<InvalidOperationException>(() =>
            new MultiSelectPrompt<string>("Pick some", Array.Empty<(string, string)>()));
    }
}
