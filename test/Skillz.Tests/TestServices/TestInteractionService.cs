using System.Collections.Immutable;
using System.Text;
using Skillz.Interaction;
using Spectre.Console;
using Spectre.Console.Rendering;

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

        // Pin the render width so panels and grids have deterministic geometry regardless of the
        // host terminal (CI vs local, narrow vs wide), which keeps the inline snapshots stable.
        _console.Profile.Width = 80;
    }

    public IReadOnlyList<string> Output => _output;

    public string OutputText => _writer.ToString();

    public Func<string, bool, bool>? OnConfirm { get; set; }

    public Func<string, IReadOnlyList<string>, string>? OnSelect { get; set; }

    public Func<string, IReadOnlyList<string>, IReadOnlyList<string>>? OnMultiSelect { get; set; }

    /// <summary>
    /// Selection hook keyed by choice index rather than label, so duplicate labels can be
    /// disambiguated in tests. Takes the message and the choice labels (in presentation order)
    /// and returns the indices to select. Takes precedence over <see cref="OnMultiSelect"/>.
    /// </summary>
    public Func<string, IReadOnlyList<string>, IReadOnlyList<int>>? OnMultiSelectByIndex { get; set; }

    /// <summary>
    /// Selection hook for the grouped prompt, keyed by (group index, item-within-group index)
    /// rather than label, so duplicate labels across groups can be disambiguated in tests. Takes
    /// the message and the group structure (group labels paired with their item labels, in
    /// presentation order) and returns the (group, item) pairs to select.
    /// </summary>
    public Func<
        string,
        IReadOnlyList<(string Group, IReadOnlyList<string> Items)>,
        IReadOnlyList<(int Group, int Item)>
    >? OnMultiSelectGroupedByIndex { get; set; }

    /// <summary>
    /// Captures the group structure (group labels paired with their item labels, in presentation
    /// order) handed to the most recent <c>MultiSelectGroupedAsync</c> call, so tests can assert
    /// the grouping, ordering, and child membership built by the caller.
    /// </summary>
    public IReadOnlyList<(string Group, IReadOnlyList<string> Items)>? LastGroupedStructure { get; private set; }

    /// <summary>
    /// Selection hook for <see cref="SearchableMultiSelectAsync{T}"/>. Receives the title, the
    /// always-included labels and the selectable labels (each in presentation order) and returns
    /// the indices of the selectable items to select. When null, the selectable items whose value
    /// is in <c>preSelected</c> are selected.
    /// </summary>
    public Func<
        string,
        IReadOnlyList<string>,
        IReadOnlyList<string>,
        IReadOnlyList<int>
    >? OnSearchableSelectByIndex { get; set; }

    /// <summary>
    /// Captures the pre-selected values handed to the most recent <c>MultiSelectAsync</c> or
    /// <c>SearchableMultiSelectAsync</c> call, so tests can assert last-used defaults are passed
    /// through to the prompt.
    /// </summary>
    public IReadOnlyList<object>? LastPreSelected { get; private set; }

    public void WriteLine(string text = "")
    {
        _output.Add(text);
        _console.WriteLine(text);
    }

    public void WriteMarkupLine(string markup)
    {
        _output.Add(markup);
        _console.MarkupLine(markup);
    }

    public void WriteRenderable(IRenderable renderable)
    {
        _console.Write(renderable);
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

    public Task<bool> ConfirmAsync(string message, bool defaultValue, CancellationToken cancellationToken)
    {
        var result = OnConfirm is not null ? OnConfirm(message, defaultValue) : defaultValue;
        return Task.FromResult(result);
    }

    public Task<T> SelectAsync<T>(
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

        var labels = pairs.Select(c => c.Label).ToList();
        var selectedLabel = OnSelect is not null ? OnSelect(message, labels) : labels[0];
        return Task.FromResult(pairs.First(c => c.Label == selectedLabel).Value);
    }

    public Task<ImmutableArray<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        CancellationToken cancellationToken)
        where T : notnull
        => MultiSelectAsync(message, choices, [], cancellationToken);

    public Task<ImmutableArray<T>> MultiSelectAsync<T>(
        string message,
        IEnumerable<(string Label, T Value)> choices,
        IEnumerable<T> preSelected,
        CancellationToken cancellationToken)
        where T : notnull
    {
        var pairs = choices.ToList();
        LastPreSelected = preSelected.Cast<object>().ToList();
        if (pairs.Count == 0)
        {
            return Task.FromResult<ImmutableArray<T>>([]);
        }

        var labels = pairs.Select(c => c.Label).ToList();

        // Index-keyed hook wins so tests can target an exact choice even when two share a label.
        if (OnMultiSelectByIndex is not null)
        {
            var indices = OnMultiSelectByIndex(message, labels);
            ImmutableArray<T> byIndex = [.. indices.Order().Select(i => pairs[i].Value)];
            return Task.FromResult(byIndex);
        }

        var selectedLabels = OnMultiSelect is not null ? OnMultiSelect(message, labels) : Array.Empty<string>();
        var selectedSet = new HashSet<string>(selectedLabels, StringComparer.Ordinal);
        ImmutableArray<T> result = [.. pairs.Where(c => selectedSet.Contains(c.Label)).Select(c => c.Value)];
        return Task.FromResult(result);
    }

    public Task<ImmutableArray<T>> MultiSelectGroupedAsync<T>(
        string message,
        IEnumerable<T> choices,
        Func<T, string> groupHeader,
        Func<T, string> label,
        CancellationToken cancellationToken)
        where T : notnull
    {
        // Group the same way the real service does (GroupBy on the header selector, source order
        // preserved) so tests can assert the headers, ordering, and membership the caller produced.
        var groups = choices.GroupBy(groupHeader).Select(g => (Group: g.Key, Items: g.ToList())).ToList();

        var structure = groups.Select(g => (g.Group, (IReadOnlyList<string>)g.Items.Select(label).ToList())).ToList();
        LastGroupedStructure = structure;

        if (groups.Count == 0)
        {
            return Task.FromResult<ImmutableArray<T>>([]);
        }

        // The hook addresses items by (group, item-within-group) index so duplicate labels never
        // collide; resolve each pair back to its value in flattened presentation order.
        IReadOnlyList<(int Group, int Item)> selected = OnMultiSelectGroupedByIndex?.Invoke(message, structure) ?? [];
        ImmutableArray<T> result =
        [
            .. groups
                .SelectMany((g, gi) => g.Items.Select((item, ii) => (gi, ii, item)))
                .Where(x => selected.Contains((x.gi, x.ii)))
                .Select(x => x.item)
        ];
        return Task.FromResult(result);
    }

    public Task<ImmutableArray<T>> SearchableMultiSelectAsync<T>(
        string title,
        IReadOnlyList<SearchableSection<T>> sections,
        IEnumerable<T> preSelected,
        CancellationToken cancellationToken)
        where T : notnull
    {
        var preSelectedList = preSelected.ToList();
        LastPreSelected = preSelectedList.Cast<object>().ToList();

        // Always-included values are part of the result regardless of any selection, in section order.
        var alwaysIncluded = sections
            .Where(s => s.AlwaysIncluded)
            .SelectMany(s => s.Items)
            .Select(item => item.Value)
            .ToList();

        // The selectable items form one flat, index-addressable list (section order).
        var selectable = sections.Where(s => !s.AlwaysIncluded).SelectMany(s => s.Items).ToList();

        IReadOnlyList<int> selectedIndices;
        if (OnSearchableSelectByIndex is not null)
        {
            var alwaysIncludedLabels = sections
                .Where(s => s.AlwaysIncluded)
                .SelectMany(s => s.Items)
                .Select(item => item.Label)
                .ToList();
            var selectableLabels = selectable.Select(item => item.Label).ToList();
            selectedIndices = OnSearchableSelectByIndex(title, alwaysIncludedLabels, selectableLabels);
        }
        else
        {
            var preSelectedSet = new HashSet<T>(preSelectedList);
            selectedIndices = Enumerable
                .Range(0, selectable.Count)
                .Where(i => preSelectedSet.Contains(selectable[i].Value))
                .ToList();
        }

        var builder = ImmutableArray.CreateBuilder<T>();
        builder.AddRange(alwaysIncluded);
        foreach (var index in selectedIndices.Order())
        {
            builder.Add(selectable[index].Value);
        }

        return Task.FromResult(builder.ToImmutable());
    }
}
