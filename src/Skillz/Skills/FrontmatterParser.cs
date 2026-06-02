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
    [GeneratedRegex(@"\A---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)\z")]
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

        var result = new Dictionary<string, object>();
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
