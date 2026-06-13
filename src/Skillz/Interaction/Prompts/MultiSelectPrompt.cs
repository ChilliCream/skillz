using System.Collections.Immutable;
using Spectre.Console;

namespace Skillz.Interaction.Prompts;

internal static class MultiSelectInstructions
{
    // Shown beneath the multi-select prompts. A choice is required, so pressing enter while nothing is
    // selected just leaves the prompt sitting there - spell out that you select with space first.
    internal const string Text =
        "[grey](Press [blue]<space>[/] to select, then [green]<enter>[/] to confirm - use [blue]j/k[/] or arrows to move)[/]";
}

/// <summary>
/// A flat multi-choice prompt over Spectre's <see cref="MultiSelectionPrompt{T}"/>. Selection is not
/// required at the widget level; the re-show-until-non-empty policy is a composed decorator.
/// </summary>
internal sealed class MultiSelectPrompt<T> : IPrompt<ImmutableArray<T>>
    where T : notnull
{
    private readonly List<(string Label, T Value)> _pairs;
    private readonly MultiSelectionPrompt<int> _prompt;

    public MultiSelectPrompt(string message, IEnumerable<(string Label, T Value)> choices)
    {
        _pairs = choices.ToList();
        if (_pairs.Count == 0)
        {
            // Spectre cannot show a choiceless prompt, and an empty selection wrapped in
            // RequireNonEmpty would re-show forever. Fail fast: callers gate the empty case.
            throw new InvalidOperationException("A multi-select prompt requires at least one choice.");
        }

        // Present the index into '_pairs' (not the label) so identical labels can't cross-select;
        // each returned index maps back to exactly one value.
        _prompt = new MultiSelectionPrompt<int>()
            .Title(Markup.Escape(message))
            .PageSize(20)
            .NotRequired()
            .InstructionsText(MultiSelectInstructions.Text)
            .UseConverter(i => _pairs[i].Label);

        for (var i = 0; i < _pairs.Count; i++)
        {
            _prompt.AddChoices(i);
        }
    }

    public async Task<ImmutableArray<T>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var selectedIndices = await console.PromptAsync(_prompt, cancellationToken);
        return [.. selectedIndices.Order().Select(i => _pairs[i].Value)];
    }
}
