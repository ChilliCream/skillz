using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text;
using Skillz.Utils;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Interaction;

internal sealed class ConsoleInteractionService(IAnsiConsole? console = null) : IInteractionService
{
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;

    // The multi-select list prompts also accept Vim-style j/k for down/up. The single-select prompt
    // enables search (letters are query text there), so it keeps the plain console below.
    private readonly IAnsiConsole _vimConsole = new VimNavConsole(console ?? AnsiConsole.Console);

    // Shown beneath the multi-select prompts. A choice is required, so pressing enter while nothing is
    // selected just leaves the prompt sitting there - spell out that you select with space first.
    private const string MultiSelectInstructions =
        "[grey](Press [blue]<space>[/] to select, then [green]<enter>[/] to confirm - use [blue]j/k[/] or arrows to move)[/]";

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
            .NotRequired()
            .InstructionsText(MultiSelectInstructions)
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

        var selectedIndices = await ShowRequiringSelectionAsync(prompt, cancellationToken);
        return [.. selectedIndices.Order().Select(i => pairs[i].Value)];
    }

    public async Task<ImmutableArray<T>> MultiSelectGroupedAsync<T>(
        string message,
        IEnumerable<T> choices,
        Func<T, string> groupHeader,
        Func<T, string> label,
        CancellationToken cancellationToken)
        where T : notnull
    {
        // Spectre identifies every node by an int and markup-parses whatever the converter returns.
        // So we hand it integer handles - never labels - which keeps identical labels from
        // cross-selecting, and we Markup.Escape the converted text so a '[' in a name can't crash the
        // render. Leaves take non-negative handles (an index into 'values'); headers take negative
        // ones, so a handle's sign alone tells the converter and the result which kind it is.
        var values = new List<T>();
        var headers = new List<string>();

        var prompt = new MultiSelectionPrompt<int>()
            .Title(Markup.Escape(message))
            .Mode(SelectionMode.Leaf)
            .PageSize(20)
            .NotRequired()
            .InstructionsText(MultiSelectInstructions);

        foreach (var group in choices.GroupBy(groupHeader))
        {
            headers.Add(group.Key);

            var childHandles = new List<int>();
            foreach (var item in group)
            {
                childHandles.Add(values.Count);
                values.Add(item);
            }

            prompt.AddChoiceGroup(-headers.Count, childHandles);
        }

        if (values.Count == 0)
        {
            return [];
        }

        prompt.UseConverter(handle => Markup.Escape(handle >= 0 ? label(values[handle]) : headers[-handle - 1]));

        var selected = await ShowRequiringSelectionAsync(prompt, cancellationToken);

        // Leaf mode returns only leaf handles, each an index into 'values' (header sentinels are never
        // returned). Order the handles, not the values - a handle is the item's position in the
        // flattened list, so ascending handles give presentation order, and T has no natural order.
        return [.. selected.Order().Select(handle => values[handle])];
    }

    // The multi-select prompts are NotRequired, so pressing enter with nothing selected returns an
    // empty list here instead of being silently ignored (Spectre's Required mode gives no feedback).
    // We flag that loudly and re-show the prompt - it keeps the user's state, so they just continue.
    private async Task<ImmutableArray<int>> ShowRequiringSelectionAsync(
        MultiSelectionPrompt<int> prompt,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var selected = await _vimConsole.PromptAsync(prompt, cancellationToken);
            if (selected.Count > 0)
            {
                return [.. selected];
            }

            _console.MarkupLine("[bold yellow]Select at least one with <space> before pressing enter.[/]");
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async Task<ImmutableArray<T>> SearchableMultiSelectAsync<T>(
        string title,
        IReadOnlyList<SearchableSection<T>> sections,
        IEnumerable<T> preSelected,
        CancellationToken cancellationToken)
        where T : notnull
    {
        // Defensive: a non-interactive console can't drive the key loop. The executor never calls
        // this path non-interactively, but if it ever does, fall back to the guaranteed selection.
        if (!_console.Profile.Capabilities.Interactive)
        {
            var selectableValues = sections
                .Where(s => !s.AlwaysIncluded)
                .SelectMany(s => s.Items)
                .Select(item => item.Value)
                .ToHashSet();

            return sections
                .Where(s => s.AlwaysIncluded)
                .SelectMany(y => y.Items.Select(item => item.Value))
                .Concat(preSelected.Where(selectableValues.Contains))
                .ToImmutableArray();
        }

        var prompt = new SearchableMultiSelectionPrompt<T>(title, sections, preSelected);
        return await prompt.ShowAsync(_console, cancellationToken);
    }
}
