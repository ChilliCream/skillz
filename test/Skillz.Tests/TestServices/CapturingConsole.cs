using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Tests.TestServices;

/// <summary>
/// An <see cref="IAnsiConsole"/> that renders to an in-memory buffer with deterministic geometry, so
/// everything a command writes can be read back as plain text via <see cref="OutputText"/>. Pins a
/// no-ANSI, no-color, 80-column profile so the captured text is identical on every platform and CI
/// runner, which is what keeps the command snapshots stable.
/// </summary>
internal sealed class CapturingConsole : IAnsiConsole
{
    private readonly StringWriter _writer = new();
    private readonly IAnsiConsole _inner;

    public CapturingConsole()
    {
        _inner = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(_writer),

                // Spectre auto-detects CI runners via profile enrichers that run after the settings
                // above and override the capabilities we just pinned - GitHubEnricher, for one, forces
                // Ansi back on when GITHUB_ACTIONS=true. That makes [dim] and other decorations emit
                // escape codes into the captured output, so the inline snapshots pass locally but fail
                // on CI. Disabling enrichment keeps the profile exactly as configured here, everywhere.
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
            });

        // Pin the render width so panels and grids have deterministic geometry regardless of the
        // host terminal (CI vs local, narrow vs wide), which keeps the inline snapshots stable.
        _inner.Profile.Width = 80;
    }

    /// <summary>
    /// The plain text rendered to this console so far - what a user would see on their terminal.
    /// </summary>
    public string OutputText => _writer.ToString();

    public Profile Profile => _inner.Profile;

    public IAnsiConsoleCursor Cursor => _inner.Cursor;

    public IAnsiConsoleInput Input => _inner.Input;

    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

    public RenderPipeline Pipeline => _inner.Pipeline;

    public void Clear(bool home) => _inner.Clear(home);

    public void Write(IRenderable renderable) => _inner.Write(renderable);
}
