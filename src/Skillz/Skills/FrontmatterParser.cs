using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Skillz.Skills;

/// <summary>Holds the parsed YAML metadata and the body content that follows the frontmatter block.</summary>
/// <param name="Data">Key/value pairs extracted from the YAML frontmatter.</param>
/// <param name="Content">The document body after the closing <c>---</c> delimiter.</param>
internal record FrontmatterResult(Dictionary<string, object> Data, string Content);

/// <summary>Parses YAML frontmatter delimited by <c>---</c> from a raw skill document.</summary>
/// <remarks>
/// Frontmatter is fetched from arbitrary sources, so parsing is bounded on three axes to keep a
/// single document from consuming unbounded memory, CPU, or stack: <see cref="MaxInputBytes"/> caps
/// the raw input, <see cref="MaxNodeCount"/> caps the number of materialized nodes (which neutralizes
/// anchor/alias expansion that would otherwise re-materialize each expansion into fresh objects), and
/// <see cref="MaxDepth"/> bounds nesting so recursion cannot overflow the stack - a
/// <see cref="StackOverflowException"/> is uncatchable and must be prevented rather than caught. Each
/// bound throws a <see cref="FormatException"/>, which the call site degrades to "skip this skill".
/// </remarks>
internal static partial class FrontmatterParser
{
    /// <summary>
    /// Maximum size, in bytes, of a frontmatter document accepted before parsing. Legitimate
    /// frontmatter is a few hundred bytes, so 1 MB leaves ample headroom while bounding parser work
    /// on the input size.
    /// </summary>
    private const int MaxInputBytes = 1024 * 1024;

    /// <summary>
    /// Maximum number of nodes <see cref="ConvertNode"/> may materialize for a single document,
    /// bounding anchor/alias expansion.
    /// </summary>
    private const int MaxNodeCount = 100_000;

    /// <summary>
    /// Maximum nesting depth converted. Frontmatter is shallow by nature, so 64 levels is generous
    /// while keeping recursion within the stack.
    /// </summary>
    private const int MaxDepth = 64;

    // Parses a YAML-style frontmatter document into two capture groups: the frontmatter body
    // (group 1) and the remaining document content (group 2).
    //
    // Pattern breakdown:
    //   \A            Anchor to the absolute start of the string (unlike ^, never matches after a
    //                 newline). Guarantees the fence we find is THE opening fence, not one mid-document.
    //   \s*           Tolerates blank lines / leading spaces before the opening fence (common editor
    //                 artifacts). The '---' must still be the first non-whitespace content - a '---'
    //                 that appears after real content is body text, not an opening fence.
    //   ---\r?\n      The opening fence, followed by either a Windows (\r\n) or Unix (\n) line ending.
    //   ([\s\S]*?)    Group 1: the frontmatter body. [\s\S] matches ANY char including newlines
    //                 (unlike '.'); *? is lazy so it stops at the FIRST closing fence, not the last.
    //   \r?\n---\r?\n? The closing fence on its own line. The trailing \n is optional (\r?\n?) so a
    //                 file that ends immediately after the closing '---' still matches.
    //   ([\s\S]*)     Group 2: everything after the frontmatter - the actual document body.
    //   \z            Anchor to the absolute end of the string (unlike \Z, won't match before a
    //                 trailing newline), so group 2 captures the body in full.
    [GeneratedRegex(@"\A\s*---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)\z")]
    private static partial Regex FrontmatterRegex();

    /// <summary>
    /// Splits <paramref name="raw"/> into its YAML metadata and body content.
    /// Returns an empty metadata dictionary and the original string unchanged when no frontmatter block is found.
    /// </summary>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="raw"/> exceeds <see cref="MaxInputBytes"/>, or when the document's
    /// node count or nesting depth exceeds <see cref="MaxNodeCount"/> or <see cref="MaxDepth"/>.
    /// </exception>
    public static FrontmatterResult Parse(string raw)
    {
        if (raw.Length > MaxInputBytes)
        {
            throw new FormatException("Frontmatter exceeds the maximum supported size.");
        }

        var match = FrontmatterRegex().Match(raw);
        if (!match.Success)
        {
            return new FrontmatterResult([], raw);
        }

        var yaml = match.Groups[1].Value;
        var content = match.Groups[2].Value;

        return new FrontmatterResult(ParseMapping(yaml), content);
    }

    private static Dictionary<string, object> ParseMapping(string yaml)
    {
        using var reader = new StringReader(yaml);

        var stream = new YamlStream();
        stream.Load(reader);

        if (stream.Documents.Count == 0
            || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return [];
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var nodeCount = 0;
        foreach (var entry in root.Children)
        {
            // Non-scalar keys (sequences, mappings) require the explicit `?` block syntax
            // and never appear in frontmatter - skip them silently.
            if (entry.Key is YamlScalarNode { Value: { } key })
            {
                result[key] = ConvertNode(entry.Value, depth: 0, ref nodeCount);
            }
        }

        return result;
    }

    private static object ConvertNode(YamlNode node, int depth, ref int nodeCount)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("Frontmatter nesting depth exceeds the supported limit.");
        }

        if (++nodeCount > MaxNodeCount)
        {
            throw new FormatException("Frontmatter node count exceeds the supported limit.");
        }

        switch (node)
        {
            case YamlMappingNode mapping:
                var map = new Dictionary<object, object>();
                foreach (var entry in mapping.Children)
                {
                    // Non-scalar keys (sequences, mappings) require the explicit `?` block syntax
                    // and never appear in frontmatter - skip them silently.
                    if (entry.Key is YamlScalarNode { Value: { } key })
                    {
                        map[key] = ConvertNode(entry.Value, depth + 1, ref nodeCount);
                    }
                }

                return map;

            case YamlSequenceNode sequence:
                var list = new List<object>();
                foreach (var item in sequence.Children)
                {
                    list.Add(ConvertNode(item, depth + 1, ref nodeCount));
                }

                return list;

            case YamlScalarNode scalar:
                return scalar.Value ?? string.Empty;

            default:
                return string.Empty;
        }
    }
}
