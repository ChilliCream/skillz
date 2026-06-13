using Spectre.Console;

namespace Skillz.Interaction.Decorators;

/// <summary>
/// Returns a precomputed default when the console cannot show an interactive prompt, and otherwise
/// delegates to the wrapped prompt. Spectre's selection prompts require BOTH an interactive terminal
/// AND ANSI support, and they fail independently: a redirected or CI stream is not interactive, while a
/// real TTY with TERM=dumb/unset has no ANSI - either one makes the prompt throw. Falling back to the
/// default on either gap keeps headless and dumb-terminal runs deterministic instead of crashing.
/// </summary>
internal sealed class WithDefault<T>(IPrompt<T> inner, T defaultValue) : IPrompt<T>
{
    public Task<T> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var capabilities = console.Profile.Capabilities;
        if (!capabilities.Interactive || !capabilities.Ansi)
        {
            return Task.FromResult(defaultValue);
        }

        return inner.ShowAsync(console, cancellationToken);
    }
}
