using System.Collections.Immutable;
using Spectre.Console;

namespace Skillz.Interaction.Decorators;

/// <summary>
/// Re-shows the wrapped multi-select prompt until the user picks at least one item. The widget itself
/// is NotRequired, so pressing enter with nothing selected returns an empty array instead of being
/// silently ignored (Spectre's Required mode gives no feedback). This flags that loudly and re-shows -
/// the widget keeps the user's state, so they just continue.
/// </summary>
internal sealed class RequireNonEmpty<T>(IPrompt<ImmutableArray<T>> inner) : IPrompt<ImmutableArray<T>>
    where T : notnull
{
    public async Task<ImmutableArray<T>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var selected = await inner.ShowAsync(console, cancellationToken);
            if (selected.Length > 0)
            {
                return selected;
            }

            console.MarkupLine("[bold yellow]Select at least one with <space> before pressing enter.[/]");
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
