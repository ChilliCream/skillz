using System.Collections.Immutable;
using Spectre.Console.Rendering;

namespace Skillz.Interaction;

internal interface IInteractionService
{
    void WriteLine(string text = "");

    void WriteMarkupLine(string markup);

    void WriteRenderable(IRenderable renderable);

    void WriteError(string message);

    void WriteErrorPanel(string title, string message, string? tip = null);

    void WriteWarning(string message);

    void WriteSuccess(string message);

    void WriteDim(string text);

    Task<T> StatusAsync<T>(string status, Func<Task<T>> action);

    Task StatusAsync(string status, Func<Task> action);

    Task<bool> ConfirmAsync(string message, bool defaultValue, CancellationToken cancellationToken);

    Task<T> SelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken)
        where T : notnull;

    Task<ImmutableArray<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken)
        where T : notnull;
}
