using Spectre.Console;

namespace Skillz.Interaction.Prompts;

/// <summary>
/// A single-choice prompt over Spectre's <see cref="SelectionPrompt{T}"/> with search enabled. Like the
/// multi-selects, it presents the index into the choice list (not the label) so identical labels can
/// never cross-select; the chosen index maps back to exactly one value.
/// </summary>
internal sealed class SelectPrompt<T> : IPrompt<T>
    where T : notnull
{
    private readonly List<(string Label, T Value)> _pairs;
    private readonly SelectionPrompt<int> _prompt;

    public SelectPrompt(string message, IEnumerable<(string Label, T Value)> choices)
    {
        _pairs = choices.ToList();
        if (_pairs.Count == 0)
        {
            throw new InvalidOperationException("SelectAsync requires at least one choice.");
        }

        _prompt = new SelectionPrompt<int>()
            .Title(Markup.Escape(message))
            .EnableSearch()
            .UseConverter(i => _pairs[i].Label)
            .AddChoices(Enumerable.Range(0, _pairs.Count));
    }

    public async Task<T> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var selectedIndex = await console.PromptAsync(_prompt, cancellationToken);
        return _pairs[selectedIndex].Value;
    }
}
