using Spectre.Console;

namespace Skillz.Interaction;

/// <summary>
/// One interactive prompt over a single Spectre widget shape: it asks, then returns the answer.
/// The data the prompt offers lives in the implementation's constructor; <see cref="ShowAsync"/>
/// only needs the console to draw on and a token to cancel by. Generic decorators
/// (re-show-until-non-empty, non-interactive fallback) compose over this one shape.
/// </summary>
internal interface IPrompt<T>
{
    Task<T> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken);
}
