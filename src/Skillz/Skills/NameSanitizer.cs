using System.Text.RegularExpressions;

namespace Skillz.Skills;

internal static partial class NameSanitizer
{
    [GeneratedRegex(@"[^a-z0-9._]+")]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex(@"^[.\-]+|[.\-]+$")]
    private static partial Regex LeadingTrailingRegex();

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
