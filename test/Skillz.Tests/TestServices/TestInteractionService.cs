using System.Collections.Immutable;
using System.Text;
using Skillz.Interaction;
using Spectre.Console;

namespace Skillz.Tests.TestServices;

internal sealed class TestInteractionService : IInteractionService
{
    private readonly List<string> _output = [];
    private readonly StringWriter _writer = new();
    private readonly IAnsiConsole _console;

    public TestInteractionService()
    {
        _console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(_writer)
            });
    }

    public IAnsiConsole Console => _console;

    public IReadOnlyList<string> Output => _output;

    public string OutputText => _writer.ToString();

    public Func<string, string?, string>? OnPrompt { get; set; }

    public Func<string, bool, bool>? OnConfirm { get; set; }

    public Func<string, IReadOnlyList<string>, string>? OnSelect { get; set; }

    public Func<string, IReadOnlyList<string>, IReadOnlyList<string>>? OnMultiSelect { get; set; }

    public void WriteLine(string text = "")
    {
        _output.Add(text);
        _console.WriteLine(text);
    }

    public void WriteMarkup(string markup)
    {
        _output.Add(markup);
        _console.Markup(markup);
    }

    public void WriteMarkupLine(string markup)
    {
        _output.Add(markup);
        _console.MarkupLine(markup);
    }

    public void WriteError(string message)
    {
        _output.Add($"ERROR: {message}");
        _console.MarkupLineInterpolated($"[red]{message}[/]");
    }

    public void WriteErrorPanel(string title, string message, string? tip = null)
    {
        _output.Add($"ERROR: {title}: {message}");
        if (tip is not null)
        {
            _output.Add($"TIP: {tip}");
        }

        var content = new StringBuilder();
        content.Append($"[red]{Markup.Escape(message)}[/]");
        if (tip is not null)
        {
            content.AppendLine();
            content.AppendLine();
            content.Append($"[dim]{Markup.Escape(tip)}[/]");
        }

        _console.WriteLine();
        _console.Write(
            new Panel(new Markup(content.ToString()))
                .Header($"[bold red]{Markup.Escape(title)}[/]")
                .BorderColor(Color.Red)
                .Expand());
    }

    public void WriteWarning(string message)
    {
        _output.Add($"WARN: {message}");
        _console.MarkupLineInterpolated($"[yellow]{message}[/]");
    }

    public void WriteSuccess(string message)
    {
        _output.Add($"SUCCESS: {message}");
        _console.MarkupLineInterpolated($"[green]{message}[/]");
    }

    public void WriteDim(string text)
    {
        _output.Add(text);
        _console.MarkupLineInterpolated($"[dim]{text}[/]");
    }

    public Task<T> StatusAsync<T>(string status, Func<Task<T>> action)
    {
        _output.Add($"STATUS: {status}");
        return action();
    }

    public Task StatusAsync(string status, Func<Task> action)
    {
        _output.Add($"STATUS: {status}");
        return action();
    }

    public Task<string> PromptAsync(
        string message,
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var result = OnPrompt is not null ? OnPrompt(message, defaultValue) : defaultValue ?? string.Empty;
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmAsync(
        string message,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
    {
        var result = OnConfirm is not null ? OnConfirm(message, defaultValue) : defaultValue;
        return Task.FromResult(result);
    }

    public Task<T> SelectAsync<T>(
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

        var labels = pairs.Select(c => c.Label).ToList();
        var selectedLabel = OnSelect is not null ? OnSelect(message, labels) : labels[0];
        return Task.FromResult(pairs.First(c => c.Label == selectedLabel).Value);
    }

    public Task<ImmutableArray<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var pairs = choices.ToList();
        if (pairs.Count == 0)
        {
            return Task.FromResult<ImmutableArray<T>>([]);
        }

        var labels = pairs.Select(c => c.Label).ToList();
        var selectedLabels = OnMultiSelect is not null ? OnMultiSelect(message, labels) : Array.Empty<string>();
        var selectedSet = new HashSet<string>(selectedLabels, StringComparer.Ordinal);
        ImmutableArray<T> result = [.. pairs.Where(c => selectedSet.Contains(c.Label)).Select(c => c.Value)];
        return Task.FromResult(result);
    }
}
