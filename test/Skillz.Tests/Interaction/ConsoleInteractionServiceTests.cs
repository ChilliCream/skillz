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

        var choices = new (string Label, string Value)[]
        {
            ("duplicate", "first"),
            ("duplicate", "second")
        };

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

        var choices = new (string Label, string Value)[]
        {
            ("alpha", "alpha"),
            ("beta", "beta"),
            ("gamma", "gamma")
        };

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

        var choices = new (string Label, string Value)[]
        {
            ("alpha", "alpha"),
            ("beta", "beta"),
            ("gamma", "gamma")
        };

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
}
