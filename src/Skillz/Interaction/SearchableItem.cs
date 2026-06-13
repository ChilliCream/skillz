namespace Skillz.Interaction;

/// <summary>
/// One row of a <see cref="SearchableMultiSelectionPrompt{T}"/>. <paramref name="Label"/>
/// is what the user sees and searches; <paramref name="Value"/> is what comes back when selected.
/// <paramref name="Mandatory"/> marks an item the user cannot opt out of (e.g. universal agents): it
/// starts selected, renders its marker in blue, and the toggle key cannot deselect it. <paramref name="Note"/> is
/// an optional dim trailing tag that is also matched by the search query.
/// </summary>
internal sealed record SearchableItem<T>(T Value, string Label, bool Mandatory = false, string? Note = null)
    where T : notnull;
