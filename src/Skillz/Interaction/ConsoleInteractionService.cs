using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Interaction;

internal sealed class ConsoleInteractionService(IAnsiConsole? console = null) : IInteractionService
{
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;

    public void WriteLine(string text = "")
    {
        _console.WriteLine(text);
    }

    public void WriteMarkupLine(string markup)
    {
        _console.MarkupLine(markup);
    }

    public void WriteRenderable(IRenderable renderable)
    {
        _console.Write(renderable);
    }

    public void WriteError(string message)
    {
        _console.MarkupLineInterpolated($"[red]{message}[/]");
    }

    public void WriteErrorPanel(string title, string message, string? tip = null)
    {
        var content = new StringBuilder();
        content.Append($"[red]{Markup.Escape(message)}[/]");
        if (tip is not null)
        {
            content.AppendLine();
            content.AppendLine();
            content.Append($"[dim]{Markup.Escape(tip)}[/]");
        }

        WriteLine();
        _console.Write(
            new Panel(new Markup(content.ToString()))
                .Header($"[bold red]{Markup.Escape(title)}[/]")
                .BorderColor(Color.Red)
                .Expand());
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

    public async Task<T> StatusAsync<T>(string status, Func<Task<T>> action)
    {
        try
        {
            return await _console.Status().StartAsync(status, _ => action());
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner)
        {
            // Spectre's Status spinner faults the action onto a background task and surfaces it
            // wrapped; unwrap here so callers see the real exception, not the wrapper.
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw; // unreachable
        }
    }

    public async Task StatusAsync(string status, Func<Task> action)
    {
        try
        {
            await _console.Status().StartAsync(status, _ => action());
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
        }
    }

    public Task<bool> ConfirmAsync(string message, bool defaultValue, CancellationToken cancellationToken)
    {
        var prompt = new ConfirmationPrompt(Markup.Escape(message)) { DefaultValue = defaultValue };

        return _console.PromptAsync(prompt, cancellationToken);
    }

    public async Task<T> SelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken)
        where T : notnull
    {
        var pairs = choices.ToList();
        if (pairs.Count == 0)
        {
            throw new InvalidOperationException("SelectAsync requires at least one choice.");
        }

        // The choice presented to Spectre is the index into 'pairs', not the label, so identical
        // labels can never cross-select: the selected index maps back to exactly one value.
        var prompt = new SelectionPrompt<int>()
            .Title(Markup.Escape(message))
            .EnableSearch()
            .UseConverter(i => pairs[i].Label)
            .AddChoices(Enumerable.Range(0, pairs.Count));

        var selectedIndex = await _console.PromptAsync(prompt, cancellationToken);
        return pairs[selectedIndex].Value;
    }

    public Task<ImmutableArray<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken)
        where T : notnull
        => MultiSelectAsync(message, choices, [], cancellationToken);

    public async Task<ImmutableArray<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        IEnumerable<T> preSelected,
        CancellationToken cancellationToken)
        where T : notnull
    {
        var pairs = choices.ToList();
        if (pairs.Count == 0)
        {
            return [];
        }

        var preSelectedSet = new HashSet<T>(preSelected);

        // Present the index into 'pairs' (not the label) so identical labels can't cross-select;
        // each returned index maps back to exactly one value.
        var prompt = new MultiSelectionPrompt<int>()
            .Title(Markup.Escape(message))
            .PageSize(20)
            .UseConverter(i => pairs[i].Label);

        for (var i = 0; i < pairs.Count; i++)
        {
            var isSelected = preSelectedSet.Contains(pairs[i].Value);
            prompt.AddChoices(
                i,
                item =>
                {
                    if (isSelected)
                    {
                        item.Select();
                    }
                });
        }

        var selectedIndices = await _console.PromptAsync(prompt, cancellationToken);
        return [.. selectedIndices.Order().Select(i => pairs[i].Value)];
    }
}
