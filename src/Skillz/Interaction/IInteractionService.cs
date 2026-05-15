using Spectre.Console;

namespace Skillz.Interaction;

internal interface IInteractionService
{
    IAnsiConsole Console { get; }

    void WriteLine(string text = "");

    void WriteMarkup(string markup);

    void WriteMarkupLine(string markup);

    void WriteError(string message);

    void WriteWarning(string message);

    void WriteSuccess(string message);

    void WriteDim(string text);

    Task<T> StatusAsync<T>(string status, Func<Task<T>> action);

    Task StatusAsync(string status, Func<Task> action);

    Task<string> PromptAsync(string message, string? defaultValue = null, CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(string message, bool defaultValue = false, CancellationToken cancellationToken = default);

    Task<T> SelectAsync<T>(string message, IEnumerable<(string Label, T Value)> choices, CancellationToken cancellationToken = default)
        where T : notnull;

    Task<IReadOnlyList<T>> MultiSelectAsync<T>(string message, IEnumerable<(string Label, T Value)> choices, CancellationToken cancellationToken = default)
        where T : notnull;
}
