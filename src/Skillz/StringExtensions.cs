namespace Skillz;

/// <summary>
/// Ergonomic wrappers over the <see cref="StringComparison"/>-taking string methods so call
/// sites read as <c>value.StartsWithOrdinal(prefix)</c> instead of
/// <c>value.StartsWith(prefix, StringComparison.Ordinal)</c>. Each wrapper forwards verbatim to
/// the underlying method with the named comparison; semantics (including null handling for the
/// static <see cref="string.Equals(string, string, StringComparison)"/>) are unchanged.
/// </summary>
internal static class StringExtensions
{
    public static bool EqualsOrdinal(this string? value, string? other)
        => string.Equals(value, other, StringComparison.Ordinal);

    public static bool EqualsOrdinalIgnoreCase(this string? value, string? other)
        => string.Equals(value, other, StringComparison.OrdinalIgnoreCase);

    public static bool StartsWithOrdinal(this string value, string prefix)
        => value.StartsWith(prefix, StringComparison.Ordinal);

    public static bool EndsWithOrdinal(this string value, string suffix)
        => value.EndsWith(suffix, StringComparison.Ordinal);

    public static bool EndsWithOrdinalIgnoreCase(this string value, string suffix)
        => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    public static bool ContainsOrdinal(this string value, string substring)
        => value.Contains(substring, StringComparison.Ordinal);

    public static bool ContainsOrdinal(this string value, char character)
        => value.Contains(character, StringComparison.Ordinal);

    public static int IndexOfOrdinal(this string value, char character)
        => value.IndexOf(character, StringComparison.Ordinal);

    public static int IndexOfOrdinal(this string value, string substring)
        => value.IndexOf(substring, StringComparison.Ordinal);

    /// <summary>
    /// Concatenates the sequence using <paramref name="separator"/> between each element.
    /// Equivalent to <c>string.Join(separator, values)</c>, read as <c>values.Join(", ")</c>.
    /// </summary>
    public static string Join(this IEnumerable<string> values, string separator)
        => string.Join(separator, values);

    /// <summary>
    /// Produces a title-cased copy of <paramref name="value"/>: splits on <c>-</c>, <c>_</c>, and
    /// space, upper-cases the first character of each word, and rejoins with single spaces. Returns
    /// the input unchanged when it is null or empty.
    /// </summary>
    public static string ToTitleCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var parts = value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
