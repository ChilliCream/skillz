using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Utils;

/// <summary>
/// Wraps an <see cref="IAnsiConsole"/> so its input also accepts Vim-style j/k navigation. Everything
/// but <see cref="Input"/> is forwarded unchanged, so rendering and capabilities are untouched.
/// </summary>
internal sealed class VimNavConsole(IAnsiConsole inner) : IAnsiConsole
{
    public Profile Profile => inner.Profile;

    public IAnsiConsoleCursor Cursor => inner.Cursor;

    public IAnsiConsoleInput Input { get; } = new VimNavInput(inner.Input);

    public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

    public RenderPipeline Pipeline => inner.Pipeline;

    public void Clear(bool home) => inner.Clear(home);

    public void Write(IRenderable renderable) => inner.Write(renderable);
}

/// <summary>
/// Translates Vim-style j/k into Down/Up arrows. The multi-select list prompts have no search box, so
/// letters are otherwise unused there - this only adds bindings, it never steals an existing one.
/// </summary>
internal sealed class VimNavInput(IAnsiConsoleInput inner) : IAnsiConsoleInput
{
    private static readonly ConsoleKeyInfo s_down = new('\0', ConsoleKey.DownArrow, false, false, false);
    private static readonly ConsoleKeyInfo s_up = new('\0', ConsoleKey.UpArrow, false, false, false);

    public bool IsKeyAvailable() => inner.IsKeyAvailable();

    public ConsoleKeyInfo? ReadKey(bool intercept) => Translate(inner.ReadKey(intercept));

    public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        => Translate(await inner.ReadKeyAsync(intercept, cancellationToken));

    private static ConsoleKeyInfo? Translate(ConsoleKeyInfo? key)
        => key?.KeyChar switch
        {
            'j' => s_down,
            'k' => s_up,
            _ => key
        };
}
