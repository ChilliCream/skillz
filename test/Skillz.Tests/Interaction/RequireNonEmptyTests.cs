using System.Collections.Immutable;
using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;
using IPrompt = Skillz.Interaction.IPrompt<System.Collections.Immutable.ImmutableArray<string>>;

namespace Skillz.Tests.Interaction;

public class RequireNonEmptyTests
{
    // Hands back a scripted result on each ShowAsync call and counts how many times it was shown, so a
    // test can assert the decorator re-showed the inner prompt exactly as expected.
    private sealed class ScriptedPrompt(params ImmutableArray<string>[] results) : IPrompt
    {
        public int ShowCount { get; private set; }

        public Task<ImmutableArray<string>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
        {
            var result = results[ShowCount];
            ShowCount++;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task ShowAsync_Should_ReshowUntilNonEmpty_When_FirstSelectionsAreEmpty()
    {
        // Arrange: two empty selections then a non-empty one - the decorator must keep re-showing.
        using var console = new TestConsole();
        var inner = new ScriptedPrompt([], [], ["chosen"]);
        var prompt = new RequireNonEmpty<string>(inner);

        // Act
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: it returned the first non-empty selection after showing the inner prompt three times.
        Assert.Equal(["chosen"], result);
        Assert.Equal(3, inner.ShowCount);
    }

    [Fact]
    public async Task ShowAsync_Should_ReturnImmediately_When_FirstSelectionIsNonEmpty()
    {
        using var console = new TestConsole();
        var inner = new ScriptedPrompt(["a", "b"]);
        var prompt = new RequireNonEmpty<string>(inner);

        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], result);
        Assert.Equal(1, inner.ShowCount);
    }

    [Fact]
    public async Task ShowAsync_Should_NudgeOnEmptySelection_When_Reshowing()
    {
        // Arrange: one empty selection forces a re-show, which must print the highlighted nudge.
        using var console = new TestConsole();
        var inner = new ScriptedPrompt([], ["chosen"]);
        var prompt = new RequireNonEmpty<string>(inner);

        // Act
        await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // Assert: the empty enter produced the "select at least one" nudge on the console.
        Assert.Contains("at least one", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowAsync_Should_ThrowOperationCanceled_When_TokenAlreadyCancelled()
    {
        // The re-show loop is bounded by the token, not while(true): a cancelled token must break out
        // before touching the inner prompt rather than spin.
        using var console = new TestConsole();
        var inner = new ScriptedPrompt(["never-reached"]);
        var prompt = new RequireNonEmpty<string>(inner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => prompt.ShowAsync(console, cts.Token));
        Assert.Equal(0, inner.ShowCount);
    }
}
