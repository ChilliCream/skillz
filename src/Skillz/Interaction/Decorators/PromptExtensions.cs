using System.Collections.Immutable;

namespace Skillz.Interaction.Decorators;

/// <summary>
/// Fluent composition for <see cref="IPrompt{T}"/> decorators, so call sites read top-down -
/// <c>picker.RequireNonEmpty().WithDefault(empty)</c> - instead of nesting the constructors
/// inside-out.
/// </summary>
internal static class PromptExtensions
{
    /// <summary>
    /// Returns <paramref name="defaultValue"/> instead of showing <paramref name="inner"/> when the
    /// console cannot drive an interactive prompt (a redirected stream or a non-ANSI terminal).
    /// </summary>
    public static IPrompt<T> WithDefault<T>(this IPrompt<T> inner, T defaultValue)
        => new WithDefault<T>(inner, defaultValue);

    /// <summary>
    /// Re-shows <paramref name="inner"/> until the user selects at least one item.
    /// </summary>
    public static IPrompt<ImmutableArray<T>> RequireNonEmpty<T>(this IPrompt<ImmutableArray<T>> inner)
        where T : notnull
        => new RequireNonEmpty<T>(inner);
}
