using System.Collections.Immutable;
using Spectre.Console;

namespace Skillz.Interaction.Prompts;

/// <summary>
/// A multi-choice prompt whose choices are grouped under headers, over Spectre's
/// <see cref="MultiSelectionPrompt{T}"/> in <see cref="SelectionMode.Leaf"/>. Selection is not required
/// at the widget level; the re-show-until-non-empty policy is a composed decorator.
/// </summary>
internal sealed class GroupedMultiSelectPrompt<T> : IPrompt<ImmutableArray<T>>
    where T : notnull
{
    private readonly List<T> _values = [];
    private readonly MultiSelectionPrompt<int> _prompt;

    public GroupedMultiSelectPrompt(
        string message,
        IEnumerable<T> choices,
        Func<T, string> groupHeader,
        Func<T, string> label)
    {
        // Spectre identifies every node by an int and markup-parses whatever the converter returns.
        // So we hand it integer handles - never labels - which keeps identical labels from
        // cross-selecting, and we Markup.Escape the converted text so a '[' in a name can't crash the
        // render. Leaves take non-negative handles (an index into '_values'); headers take negative
        // ones, so a handle's sign alone tells the converter and the result which kind it is.
        var headers = new List<string>();

        _prompt = new MultiSelectionPrompt<int>()
            .Title(Markup.Escape(message))
            .Mode(SelectionMode.Leaf)
            .PageSize(20)
            .NotRequired()
            .InstructionsText(MultiSelectInstructions.Text);

        foreach (var group in choices.GroupBy(groupHeader))
        {
            headers.Add(group.Key);

            var childHandles = new List<int>();
            foreach (var item in group)
            {
                childHandles.Add(_values.Count);
                _values.Add(item);
            }

            _prompt.AddChoiceGroup(-headers.Count, childHandles);
        }

        if (_values.Count == 0)
        {
            // No leaves means a choiceless Spectre prompt, which throws; and an empty selection wrapped
            // in RequireNonEmpty would re-show forever. Fail fast: callers gate the empty case.
            throw new InvalidOperationException("A grouped multi-select prompt requires at least one choice.");
        }

        _prompt.UseConverter(handle => Markup.Escape(handle >= 0 ? label(_values[handle]) : headers[-handle - 1]));
    }

    public async Task<ImmutableArray<T>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var selected = await console.PromptAsync(_prompt, cancellationToken);

        // Leaf mode returns only leaf handles, each an index into '_values' (header sentinels are never
        // returned). Order the handles, not the values - a handle is the item's position in the
        // flattened list, so ascending handles give presentation order, and T has no natural order.
        return [.. selected.Order().Select(handle => _values[handle])];
    }
}
