using Skillz.Interaction;
using Skillz.Net;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Commands;

internal sealed class FindCommandPrompter : IFindCommandPrompter
{
    private const int MaxVisible = 8;

    private readonly IInteractionService _interaction;
    private readonly ISkillSearchClient _searchClient;

    public FindCommandPrompter(IInteractionService interaction, ISkillSearchClient searchClient)
    {
        _interaction = interaction;
        _searchClient = searchClient;
    }

    public async Task<SearchSkill?> RunInteractiveSearchAsync(string initialQuery, CancellationToken cancellationToken)
    {
        var state = new SearchState
        {
            Query = initialQuery ?? string.Empty
        };

        if (!string.IsNullOrEmpty(state.Query))
        {
            state.Results = await SafeSearchAsync(state.Query, cancellationToken).ConfigureAwait(false);
        }

        var console = _interaction.Console;
        var stop = false;
        SearchSkill? selected = null;

        await console.Live(BuildPanel(state))
            .StartAsync(async ctx =>
            {
                ctx.Refresh();

                while (!stop && !cancellationToken.IsCancellationRequested)
                {
                    ConsoleKeyInfo key;
                    try
                    {
                        key = await ReadKeyAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (key.Key == ConsoleKey.Escape)
                    {
                        stop = true;
                        return;
                    }

                    if (key.Key == ConsoleKey.Enter)
                    {
                        if (state.Results.Count > 0
                            && state.SelectedIndex >= 0
                            && state.SelectedIndex < state.Results.Count)
                        {
                            selected = state.Results[state.SelectedIndex];
                        }

                        stop = true;
                        return;
                    }

                    if (key.Key == ConsoleKey.UpArrow)
                    {
                        state.SelectedIndex = Math.Max(0, state.SelectedIndex - 1);
                    }
                    else if (key.Key == ConsoleKey.DownArrow)
                    {
                        state.SelectedIndex = Math.Min(Math.Max(0, state.Results.Count - 1), state.SelectedIndex + 1);
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (state.Query.Length > 0)
                        {
                            state.Query = state.Query[..^1];
                            await UpdateResultsAsync(state, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else if (key.KeyChar >= ' ' && key.KeyChar <= '~')
                    {
                        state.Query += key.KeyChar;
                        await UpdateResultsAsync(state, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        continue;
                    }

                    ctx.UpdateTarget(BuildPanel(state));
                    ctx.Refresh();
                }
            })
            .ConfigureAwait(false);

        return selected;
    }

    private async Task UpdateResultsAsync(SearchState state, CancellationToken cancellationToken)
    {
        if (state.Query.Length < 2)
        {
            state.Results = Array.Empty<SearchSkill>();
            state.SelectedIndex = 0;
            return;
        }

        state.Results = await SafeSearchAsync(state.Query, cancellationToken).ConfigureAwait(false);
        state.SelectedIndex = 0;
    }

    private async Task<IReadOnlyList<SearchSkill>> SafeSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            return await _searchClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<SearchSkill>();
        }
        catch
        {
            return Array.Empty<SearchSkill>();
        }
    }

    private static IRenderable BuildPanel(SearchState state)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[grey]Search skills:[/] {Markup.Escape(state.Query)}[bold]_[/]"),
            Text.Empty
        };

        if (state.Query.Length < 2)
        {
            rows.Add(new Markup("[dim]Start typing to search (min 2 chars)[/]"));
        }
        else if (state.Results.Count == 0)
        {
            rows.Add(new Markup("[dim]No skills found[/]"));
        }
        else
        {
            var visible = state.Results.Take(MaxVisible).ToList();
            for (var i = 0; i < visible.Count; i++)
            {
                var skill = visible[i];
                var isSelected = i == state.SelectedIndex;
                var arrow = isSelected ? "[bold]>[/]" : " ";
                var name = isSelected
                    ? $"[bold]{Markup.Escape(skill.Name)}[/]"
                    : $"[grey85]{Markup.Escape(skill.Name)}[/]";
                var source = string.IsNullOrEmpty(skill.Source)
                    ? string.Empty
                    : $" [dim]{Markup.Escape(skill.Source)}[/]";
                var installs = FormatInstalls(skill.Installs);
                var installsBadge = string.IsNullOrEmpty(installs)
                    ? string.Empty
                    : $" [cyan]{Markup.Escape(installs)}[/]";

                rows.Add(new Markup($"  {arrow} {name}{source}{installsBadge}"));
            }
        }

        rows.Add(Text.Empty);
        rows.Add(new Markup("[dim]up/down navigate | enter select | esc cancel[/]"));

        return new Rows(rows);
    }

    private static string FormatInstalls(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        if (count >= 1_000_000)
        {
            return $"{(count / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}M installs";
        }

        if (count >= 1_000)
        {
            return $"{(count / 1_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}K installs";
        }

        return count == 1 ? "1 install" : $"{count} installs";
    }

    private static Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            while (!Console.KeyAvailable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(20);
            }

            return Console.ReadKey(intercept: true);
        }, cancellationToken);
    }

    private sealed class SearchState
    {
        public string Query { get; set; } = string.Empty;

        public IReadOnlyList<SearchSkill> Results { get; set; } = Array.Empty<SearchSkill>();

        public int SelectedIndex { get; set; }
    }
}
