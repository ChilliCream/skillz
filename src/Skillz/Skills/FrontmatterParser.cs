using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Skillz.Skills;

/// <summary>Holds the parsed YAML metadata and the body content that follows the frontmatter block.</summary>
/// <param name="Data">Key/value pairs extracted from the YAML frontmatter.</param>
/// <param name="Content">The document body after the closing <c>---</c> delimiter.</param>
internal record FrontmatterResult(Dictionary<string, object> Data, string Content);

/// <summary>Parses YAML frontmatter delimited by <c>---</c> from a raw skill document.</summary>
internal static partial class FrontmatterParser
{

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
    public static FrontmatterResult Parse(string raw)
    {
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
        foreach (var entry in root.Children)
        {
            // Non-scalar keys (sequences, mappings) require the explicit `?` block syntax
            // and never appear in frontmatter - skip them silently.
            if (entry.Key is YamlScalarNode { Value: { } key })
            {
                result[key] = ConvertNode(entry.Value);
            }
        }

        return result;
    }

    private static object ConvertNode(YamlNode node)
    {
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
                        map[key] = ConvertNode(entry.Value);
                    }
                }

                return map;

            case YamlSequenceNode sequence:
                var list = new List<object>();
                foreach (var item in sequence.Children)
                {
                    list.Add(ConvertNode(item));
                }

                return list;

            case YamlScalarNode scalar:
                return scalar.Value ?? string.Empty;

            default:
                return string.Empty;
        }
    }
}
