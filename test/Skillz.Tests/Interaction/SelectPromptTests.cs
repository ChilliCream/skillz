using Skillz.Interaction.Prompts;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Interaction;

public class SelectPromptTests
{
    private static TestConsole CreateInteractiveConsole()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        return console;
    }

    [Fact]
    public async Task ShowAsync_Should_Return_Second_Value_When_Labels_Are_Identical()
    {
        // Arrange: two choices share the exact same label; only the value distinguishes them.
        using var console = CreateInteractiveConsole();
        console.Input.PushKey(ConsoleKey.DownArrow); // move highlight from index 0 to index 1
        console.Input.PushKey(ConsoleKey.Enter);

        var choices = new (string Label, string Value)[] { ("duplicate", "first"), ("duplicate", "second") };
        var prompt = new SelectPrompt<string>("Pick one", choices);

        // Act
        var selected = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the second item is picked by position, not by matching the (ambiguous) label.
        Assert.Equal("second", selected);
    }

    [Fact]
    public void Constructor_Should_Throw_When_No_Choices()
    {
        // The single-select has no sensible empty state; building one with no choices is a programming error.
        Assert.Throws<InvalidOperationException>(() =>
            new SelectPrompt<string>("Pick one", Array.Empty<(string, string)>()));
    }
}
