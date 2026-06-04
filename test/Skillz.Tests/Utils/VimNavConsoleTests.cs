using Skillz.Utils;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Utils;

public class VimNavConsoleTests
{
    [Fact]
    public void ReadKey_Should_Translate_J_To_DownArrow()
    {
        var console = new TestConsole();
        console.Input.PushCharacter('j');
        var input = new VimNavInput(console.Input);

        var key = input.ReadKey(intercept: true);

        Assert.Equal(ConsoleKey.DownArrow, key?.Key);
    }

    [Fact]
    public void ReadKey_Should_Translate_K_To_UpArrow()
    {
        var console = new TestConsole();
        console.Input.PushCharacter('k');
        var input = new VimNavInput(console.Input);

        var key = input.ReadKey(intercept: true);

        Assert.Equal(ConsoleKey.UpArrow, key?.Key);
    }

    [Fact]
    public void ReadKey_Should_PassThrough_NonVimCharacter()
    {
        var console = new TestConsole();
        console.Input.PushCharacter('x');
        var input = new VimNavInput(console.Input);

        var key = input.ReadKey(intercept: true);

        // 'x' is not a navigation key, so it is handed back untouched (e.g. for search/typing).
        Assert.Equal('x', key?.KeyChar);
    }

    [Fact]
    public void ReadKey_Should_PassThrough_ArrowKey_Untouched()
    {
        var console = new TestConsole();
        console.Input.PushKey(ConsoleKey.DownArrow);
        var input = new VimNavInput(console.Input);

        var key = input.ReadKey(intercept: true);

        Assert.Equal(ConsoleKey.DownArrow, key?.Key);
    }

    [Fact]
    public async Task ReadKeyAsync_Should_Translate_K_To_UpArrow()
    {
        var console = new TestConsole();
        console.Input.PushCharacter('k');
        var input = new VimNavInput(console.Input);

        var key = await input.ReadKeyAsync(intercept: true, TestContext.Current.CancellationToken);

        Assert.Equal(ConsoleKey.UpArrow, key?.Key);
    }

    [Fact]
    public void Input_Should_Apply_Translation_Through_The_Wrapping_Console()
    {
        var inner = new TestConsole();
        inner.Input.PushCharacter('j');
        var console = new VimNavConsole(inner);

        var key = console.Input.ReadKey(intercept: true);

        Assert.Equal(ConsoleKey.DownArrow, key?.Key);
    }

    [Fact]
    public void Console_Should_Forward_Profile_To_Inner()
    {
        var inner = new TestConsole();
        var console = new VimNavConsole(inner);

        // Everything but Input is forwarded unchanged, so rendering/capabilities stay identical.
        Assert.Same(inner.Profile, console.Profile);
    }
}
