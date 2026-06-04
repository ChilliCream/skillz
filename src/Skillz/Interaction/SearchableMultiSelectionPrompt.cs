using System.Collections.Immutable;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Interaction;

/// <summary>
/// A custom multi-select prompt with type-to-search, modelled on the npx-skills agent picker.
/// Spectre's <c>MultiSelectionPrompt</c> has no search box, so we drive our own key loop over a
/// <see cref="LiveDisplay"/>. "Always included" sections render as a static, non-navigable bullet
/// list; the remaining sections form one filterable, toggleable list keyed by item index so
/// duplicate labels never cross-select.
/// </summary>
internal sealed class SearchableMultiSelectionPrompt<T> where T : notnull
{
    // Hard cap on the query length so a pathological paste cannot grow state unboundedly.
    private const int MaxQueryLength = 256;

    // Rows of the selectable list shown at once; the rest scroll with "N more" indicators.
    private const int WindowSize = 12;

    // How many always-included items to spell out before collapsing into "...and N more".
    private const int AlwaysIncludedPreviewCount = 8;

    private readonly string _title;
    private readonly IReadOnlyList<SearchableSection<T>> _sections;

    // Values that are always part of the result, in section order.
    private readonly List<T> _alwaysIncluded = [];

    // The flattened selectable items in section order; selection state is keyed by index here.
    private readonly List<(string Label, T Value)> _selectable = [];

    private readonly HashSet<int> _selected = [];

    private string _query = string.Empty;
    private int _cursor;

    public SearchableMultiSelectionPrompt(
        string title,
        IReadOnlyList<SearchableSection<T>> sections,
        IEnumerable<T> preSelected)
    {
        _title = title;
        _sections = sections;

        foreach (var section in sections)
        {
            if (section.AlwaysIncluded)
            {
                foreach (var (_, value) in section.Items)
                {
                    _alwaysIncluded.Add(value);
                }
            }
            else
            {
                _selectable.AddRange(section.Items);
            }
        }

        var preSelectedSet = new HashSet<T>(preSelected);
        for (var i = 0; i < _selectable.Count; i++)
        {
            if (preSelectedSet.Contains(_selectable[i].Value))
            {
                _selected.Add(i);
            }
        }
    }

    public async Task<ImmutableArray<T>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var hasSelectable = _selectable.Count > 0;

        // LiveDisplay already takes the console's exclusivity lock; wrapping it in our own
        // RunExclusive would re-enter that lock and trip Spectre's "interactive functions running
        // concurrently" guard. Drive Live directly, exactly as Spectre's own prompts do.
        var live = console.Live(BuildRenderable());
        live.AutoClear = true;
        live.Overflow = VerticalOverflow.Ellipsis;

        await live.StartAsync(async ctx =>
        {
            while (true)
            {
                ctx.UpdateTarget(BuildRenderable());
                ctx.Refresh();

                var keyInfo = await console.Input.ReadKeyAsync(intercept: true, cancellationToken);
                if (keyInfo is not { } key)
                {
                    // Input closed/redirected: confirm with the current state rather than hanging.
                    break;
                }

                // With no selectable items only Enter is meaningful; everything else is ignored.
                if (hasSelectable)
                {
                    if (HandleKey(key))
                    {
                        break;
                    }
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }
            }
        });

        // AutoClear wiped the live region; leave one compact line so the choice is visible in scrollback.
        console.MarkupLine($"[bold]{Markup.Escape(_title)}[/] {BuildFooter()}");

        return BuildResult();
    }

    /// <summary>Returns true when the loop should stop (Enter pressed).</summary>
    private bool HandleKey(ConsoleKeyInfo key)
    {
        var filtered = GetFilteredIndices();

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return true;

            case ConsoleKey.UpArrow:
                if (filtered.Count > 0)
                {
                    _cursor = Math.Max(0, _cursor - 1);
                }

                return false;

            case ConsoleKey.DownArrow:
                if (filtered.Count > 0)
                {
                    _cursor = Math.Min(filtered.Count - 1, _cursor + 1);
                }

                return false;

            case ConsoleKey.Spacebar:
                if (filtered.Count > 0 && _cursor >= 0 && _cursor < filtered.Count)
                {
                    var target = filtered[_cursor];
                    if (!_selected.Add(target))
                    {
                        _selected.Remove(target);
                    }
                }

                return false;

            case ConsoleKey.Backspace:
                if (_query.Length > 0)
                {
                    _query = _query[..^1];
                    _cursor = 0;
                }

                return false;

            case ConsoleKey.Escape:
                // Clear the query if there is one; never cancel the prompt.
                if (_query.Length > 0)
                {
                    _query = string.Empty;
                    _cursor = 0;
                }

                return false;

            default:
                // Printable characters extend the query; control characters are ignored.
                if (!char.IsControl(key.KeyChar) && _query.Length < MaxQueryLength)
                {
                    _query += key.KeyChar;
                    _cursor = 0;
                }

                return false;
        }
    }

    private List<int> GetFilteredIndices()
    {
        if (_query.Length == 0)
        {
            return [.. Enumerable.Range(0, _selectable.Count)];
        }

        var result = new List<int>();
        for (var i = 0; i < _selectable.Count; i++)
        {
            if (_selectable[i].Label.Contains(_query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(i);
            }
        }

        return result;
    }

    private ImmutableArray<T> BuildResult()
    {
        var builder = ImmutableArray.CreateBuilder<T>();
        builder.AddRange(_alwaysIncluded);

        // Selectable values in item order, regardless of the current filter.
        for (var i = 0; i < _selectable.Count; i++)
        {
            if (_selected.Contains(i))
            {
                builder.Add(_selectable[i].Value);
            }
        }

        return builder.ToImmutable();
    }

    private IRenderable BuildRenderable()
    {
        var rows = new List<IRenderable> { new Markup($"[bold]{Markup.Escape(_title)}[/]") };

        foreach (var section in _sections)
        {
            if (!section.AlwaysIncluded)
            {
                continue;
            }

            rows.Add(new Rule($"[dim]{Markup.Escape(section.Header)}[/]") { Justification = Justify.Left });
            AppendAlwaysIncluded(rows, section.Items);
        }

        var selectableSection = _sections.FirstOrDefault(s => !s.AlwaysIncluded);
        if (selectableSection is not null)
        {
            rows.Add(new Rule($"[dim]{Markup.Escape(selectableSection.Header)}[/]") { Justification = Justify.Left });
        }

        rows.Add(new Markup($"[dim]Search:[/] {Markup.Escape(_query)}"));
        AppendSelectable(rows);
        rows.Add(new Markup("[dim]↑↓ move · space select · type to search · enter confirm[/]"));
        rows.Add(new Markup(BuildFooter()));

        return new Rows(rows);
    }

    private static void AppendAlwaysIncluded(List<IRenderable> rows, IReadOnlyList<(string Label, T Value)> items)
    {
        var shown = Math.Min(AlwaysIncludedPreviewCount, items.Count);
        for (var i = 0; i < shown; i++)
        {
            rows.Add(new Markup($"  [dim]•[/] {Markup.Escape(items[i].Label)}"));
        }

        if (items.Count > shown)
        {
            rows.Add(new Markup($"  [dim]...and {items.Count - shown} more[/]"));
        }
    }

    private void AppendSelectable(List<IRenderable> rows)
    {
        var filtered = GetFilteredIndices();
        if (filtered.Count == 0)
        {
            rows.Add(new Markup("  [dim](no matches)[/]"));
            return;
        }

        if (_cursor >= filtered.Count)
        {
            _cursor = filtered.Count - 1;
        }

        // Scroll the window so the cursor stays visible.
        var windowStart = 0;
        if (filtered.Count > WindowSize)
        {
            windowStart = Math.Clamp(_cursor - (WindowSize / 2), 0, filtered.Count - WindowSize);
        }

        var windowEnd = Math.Min(filtered.Count, windowStart + WindowSize);

        if (windowStart > 0)
        {
            rows.Add(new Markup($"  [dim]↑ {windowStart} more[/]"));
        }

        for (var pos = windowStart; pos < windowEnd; pos++)
        {
            var itemIndex = filtered[pos];
            var isCursor = pos == _cursor;
            var isSelected = _selected.Contains(itemIndex);
            var pointer = isCursor ? "[cyan]❯[/]" : " ";
            var marker = isSelected ? "[green]●[/]" : "[dim]○[/]";
            var label = Markup.Escape(_selectable[itemIndex].Label);
            var styledLabel = isCursor ? $"[invert]{label}[/]" : label;
            rows.Add(new Markup($"{pointer} {marker} {styledLabel}"));
        }

        if (windowEnd < filtered.Count)
        {
            rows.Add(new Markup($"  [dim]↓ {filtered.Count - windowEnd} more[/]"));
        }
    }

    private string BuildFooter()
    {
        var selectedLabels = new List<string>();
        for (var i = 0; i < _selectable.Count; i++)
        {
            if (_selected.Contains(i))
            {
                selectedLabels.Add(_selectable[i].Label);
            }
        }

        if (selectedLabels.Count == 0)
        {
            return "[dim]Selected: (none)[/]";
        }

        const int previewCount = 3;
        var preview = selectedLabels.Take(previewCount).Select(Markup.Escape);
        var summary = string.Join(", ", preview);
        if (selectedLabels.Count > previewCount)
        {
            summary += $" +{selectedLabels.Count - previewCount} more";
        }

        return $"[dim]Selected:[/] {summary}";
    }
}
