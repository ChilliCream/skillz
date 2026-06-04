namespace Skillz.Interaction;

/// <summary>
/// A labelled group of items for <see cref="IInteractionService.SearchableMultiSelectAsync{T}"/>.
/// When <paramref name="AlwaysIncluded"/> is true the section is informational: its values are
/// always part of the result and its items are neither navigable nor toggleable. Otherwise the
/// items join the single searchable, toggleable list the user navigates.
/// </summary>
internal sealed record SearchableSection<T>(
    string Header,
    bool AlwaysIncluded,
    IReadOnlyList<(string Label, T Value)> Items) where T : notnull;
