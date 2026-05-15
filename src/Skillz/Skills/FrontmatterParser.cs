using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Skillz.Skills;

internal record FrontmatterResult(Dictionary<string, object> Data, string Content);

internal static partial class FrontmatterParser
{
    [GeneratedRegex(@"\A---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)\z")]
    private static partial Regex FrontmatterRegex();

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Deserializing into Dictionary<string, object> uses only built-in primitive types that YamlDotNet handles without dynamic code generation.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Dictionary<string, object> contains only primitive YAML scalar types; no user-defined types are reflected over.")]
    public static FrontmatterResult Parse(string raw)
    {
        var match = FrontmatterRegex().Match(raw);
        if (!match.Success)
        {
            return new FrontmatterResult(new Dictionary<string, object>(), raw);
        }

        var yaml = match.Groups[1].Value;
        var content = match.Groups[2].Value;

        var deserializer = new DeserializerBuilder().Build();
        var parsed = deserializer.Deserialize<Dictionary<string, object>?>(yaml);

        return new FrontmatterResult(parsed ?? new Dictionary<string, object>(), content);
    }
}
