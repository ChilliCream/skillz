using Spectre.Console;

namespace Skillz.Interaction;

internal sealed class ConsoleInteractionService : IInteractionService
{
    private readonly IAnsiConsole _console;

    public ConsoleInteractionService(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    public IAnsiConsole Console => _console;

    public void WriteLine(string text = "")
    {
        _console.WriteLine(text);
    }

    public void WriteMarkup(string markup)
    {
        _console.Markup(markup);
    }

    public void WriteMarkupLine(string markup)
    {
        _console.MarkupLine(markup);
    }

    public void WriteError(string message)
    {
        _console.MarkupLineInterpolated($"[red]{message}[/]");
    }

    public void WriteWarning(string message)
    {
        _console.MarkupLineInterpolated($"[yellow]{message}[/]");
    }

    public void WriteSuccess(string message)
    {
        _console.MarkupLineInterpolated($"[green]{message}[/]");
    }

    public void WriteDim(string text)
    {
        _console.MarkupLineInterpolated($"[dim]{text}[/]");
    }

    public Task<T> StatusAsync<T>(string status, Func<Task<T>> action)
    {
        return _console.Status().StartAsync(status, _ => action());
    }

    public Task StatusAsync(string status, Func<Task> action)
    {
        return _console.Status().StartAsync(status, _ => action());
    }

    public Task<string> PromptAsync(
        string message,
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = new TextPrompt<string>(Markup.Escape(message)) { AllowEmpty = true };

        if (defaultValue is not null)
        {
            prompt.DefaultValue(defaultValue);
        }

        return _console.PromptAsync(prompt, cancellationToken);
    }

    public Task<bool> ConfirmAsync(
        string message,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
    {
        var prompt = new ConfirmationPrompt(Markup.Escape(message)) { DefaultValue = defaultValue };

        return _console.PromptAsync(prompt, cancellationToken);
    }

    public async Task<T> SelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var pairs = choices.ToList();
        if (pairs.Count == 0)
        {
            throw new InvalidOperationException("SelectAsync requires at least one choice.");
        }

        var labels = pairs.Select(c => c.Label).ToArray();
        var prompt = new SelectionPrompt<string>().Title(Markup.Escape(message)).EnableSearch().AddChoices(labels);

        var selected = await _console.PromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        return pairs.First(c => c.Label == selected).Value;
    }

    public async Task<IReadOnlyList<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var pairs = choices.ToList();
        if (pairs.Count == 0)
        {
            return Array.Empty<T>();
        }

        var labels = pairs.Select(c => c.Label).ToArray();
        var prompt = new MultiSelectionPrompt<string>().Title(Markup.Escape(message)).PageSize(20).AddChoices(labels);

        var selected = await _console.PromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        var selectedSet = new HashSet<string>(selected, StringComparer.Ordinal);
        return pairs.Where(c => selectedSet.Contains(c.Label)).Select(c => c.Value).ToList();
    }
}
