using System.Text.RegularExpressions;

namespace Skillz.Skills;

internal static partial class TerminalSanitizer
{
    [GeneratedRegex(@"\x1b\][\s\S]*?(?:\x07|\x1b\\)")]
    private static partial Regex OscRegex();

    [GeneratedRegex(@"\x1b[P^_][\s\S]*?(?:\x1b\\)")]
    private static partial Regex DcsPmApcRegex();

    [GeneratedRegex(@"\x1b\[[\x30-\x3f]*[\x20-\x2f]*[\x40-\x7e]")]
    private static partial Regex CsiRegex();

    [GeneratedRegex(@"\x1b[\x20-\x7e]")]
    private static partial Regex SimpleEscRegex();

    [GeneratedRegex(@"[\x80-\x9f]")]
    private static partial Regex C1Regex();

    [GeneratedRegex(@"[\x00-\x06\x07\x08\x0b\x0c\x0d-\x1a\x1c-\x1f\x7f]")]
    private static partial Regex ControlRegex();

    [GeneratedRegex(@"[\r\n]+")]
    private static partial Regex NewlineRegex();

    public static string StripTerminalEscapes(string str)
    {
        str = OscRegex().Replace(str, "");
        str = DcsPmApcRegex().Replace(str, "");
        str = CsiRegex().Replace(str, "");
        str = SimpleEscRegex().Replace(str, "");
        str = C1Regex().Replace(str, "");
        str = ControlRegex().Replace(str, "");
        return str;
    }

    public static string SanitizeMetadata(string str)
    {
        return NewlineRegex().Replace(StripTerminalEscapes(str), " ").Trim();
    }
}
