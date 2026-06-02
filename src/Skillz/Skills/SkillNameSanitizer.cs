using System.Text.RegularExpressions;

namespace Skillz.Skills;

/// <summary>
/// Normalizes arbitrary skill names into safe, canonical identifiers that can be
/// used both as filesystem directory names and as deduplication keys.
/// </summary>
internal static partial class SkillNameSanitizer
{
    [GeneratedRegex(@"[^a-z0-9._]+")]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex(@"^[.\-]+|[.\-]+$")]
    private static partial Regex LeadingTrailingRegex();

    /// <summary>
    /// Sanitizes a skill name into a lowercase, filesystem-safe identifier.
    /// </summary>
    /// <remarks>
    /// The name is lowercased, any run of characters outside <c>[a-z0-9._]</c> is
    /// collapsed into a single hyphen, and leading/trailing dots and hyphens are
    /// trimmed. This neutralizes path separators and <c>..</c> segments, preventing
    /// path traversal when the result is used as a directory name. The result is
    /// capped at 255 characters to respect filesystem limits.
    /// <example>
    /// <code>
    /// SanitizeName("My Skill")        // "my-skill"
    /// SanitizeName("../evil")         // "evil"
    /// SanitizeName("foo/bar\\baz")    // "foo-bar-baz"
    /// SanitizeName("hello world!")    // "hello-world"
    /// SanitizeName("   ")             // "unnamed-skill"
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="name">The raw skill name to sanitize.</param>
    /// <returns>
    /// The sanitized name, or <c>"unnamed-skill"</c> when sanitization leaves an
    /// empty string.
    /// </returns>
    public static string SanitizeName(string name)
    {
        var result = InvalidCharsRegex().Replace(name.ToLowerInvariant(), "-");
        result = LeadingTrailingRegex().Replace(result, "");
        if (result.Length > 255)
        {
            result = result[..255];
        }

        return result.Length == 0 ? "unnamed-skill" : result;
    }
}
