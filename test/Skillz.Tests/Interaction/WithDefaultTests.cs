using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;
using IPrompt = Skillz.Interaction.IPrompt<string>;

namespace Skillz.Tests.Interaction;

public class WithDefaultTests
{
    // Returns a fixed answer and records whether the decorator delegated to it, so a test can prove the
    // decorator delegated on an interactive console and short-circuited on a non-interactive one.
    private sealed class FakePrompt(string answer) : IPrompt
    {
        public bool WasShown { get; private set; }

        public Task<string> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
        {
            WasShown = true;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task ShowAsync_Should_ReturnDefaultWithoutDelegating_When_ConsoleNonInteractive()
    {
        // Arrange: a non-interactive console cannot drive a key loop, so the decorator must return the
        // supplied default without ever touching the inner prompt.
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = false;
        var inner = new FakePrompt("interactive-answer");
        var prompt = new WithDefault<string>(inner, "headless-default");

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the default came back and the inner prompt was never shown.
        Assert.Equal("headless-default", result);
        Assert.False(inner.WasShown);
    }

    [Fact]
    public async Task ShowAsync_Should_ReturnDefaultWithoutDelegating_When_AnsiUnsupported()
    {
        // Arrange: a real interactive TTY with no ANSI support (e.g. TERM=dumb). Spectre's selection
        // prompts throw on a non-ANSI profile even though Interactive is true, so the decorator must
        // still fall back to the default rather than delegate into the throwing prompt.
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = false;
        var inner = new FakePrompt("interactive-answer");
        var prompt = new WithDefault<string>(inner, "headless-default");

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the default came back and the inner prompt was never shown.
        Assert.Equal("headless-default", result);
        Assert.False(inner.WasShown);
    }

    [Fact]
    public async Task ShowAsync_Should_DelegateToInner_When_ConsoleInteractiveAndAnsi()
    {
        // Arrange: an interactive, ANSI-capable console can ask, so the decorator must delegate and
        // return the inner prompt's answer, not the default.
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        var inner = new FakePrompt("interactive-answer");
        var prompt = new WithDefault<string>(inner, "headless-default");

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the inner prompt was shown and its answer flowed through unchanged.
        Assert.Equal("interactive-answer", result);
        Assert.True(inner.WasShown);
    }
}
