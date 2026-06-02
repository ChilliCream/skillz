using System.Text.RegularExpressions;

namespace Skillz.Skills;

/// <summary>
/// Strips ANSI/terminal escape sequences and control characters from untrusted text so
/// that skill metadata read off disk cannot inject escapes into the user's terminal.
/// </summary>
/// <remarks>
/// Each regex targets a distinct class of terminal control: OSC (Operating System
/// Command), DCS/PM/APC string sequences, CSI (Control Sequence Introducer), simple
/// two-byte escapes, C1 control bytes, and other C0 control characters. Removing all of
/// them prevents escape-sequence injection (e.g. cursor manipulation, title spoofing,
/// or hidden text) from a malicious <c>SKILL.md</c>.
/// </remarks>
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

    /// <summary>
    /// Removes all terminal escape sequences and control characters from
    /// <paramref name="str"/>, preserving ordinary printable text and newlines.
    /// </summary>
    /// <param name="str">The untrusted text to strip.</param>
    /// <returns>The text with every recognized escape and control sequence removed.</returns>
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

    /// <summary>
    /// Sanitizes a single-line metadata value (such as a skill name or description) by
    /// stripping terminal escapes and collapsing any newlines into spaces.
    /// </summary>
    /// <param name="str">The untrusted metadata value to sanitize.</param>
    /// <returns>A trimmed, single-line, escape-free string safe to print to the terminal.</returns>
    public static string SanitizeMetadata(string str)
    {
        return NewlineRegex().Replace(StripTerminalEscapes(str), " ").Trim();
    }
}
