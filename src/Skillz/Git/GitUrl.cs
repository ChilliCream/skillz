using System.Text.RegularExpressions;

namespace Skillz.Git;

/// <summary>
/// Helpers for handling git repository URLs safely, in particular scrubbing embedded
/// credentials before a URL reaches error output or logs.
/// </summary>
internal static partial class GitUrl
{
    /// <summary>
    /// Matches the <c>scheme://userinfo@</c> prefix of a URL (for example
    /// <c>https://user:token@host/repo.git</c>) so embedded credentials can be
    /// stripped before a URL is shown in an error message or log line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="RedactUrlUserInfo"/>, which replaces the matched
    /// user-info with <c>&lt;redacted&gt;</c> so we never leak a token or
    /// password. Named groups:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>scheme</c> - e.g. <c>https://</c> or
    /// <c>git+ssh://</c>; preserved as-is.</description></item>
    /// <item><description><c>userinfo</c> - everything up to and including the
    /// <c>@</c>; this is the part that gets hidden.</description></item>
    /// </list>
    /// <para>
    /// The user-info group is greedy and runs up to the <em>last</em> <c>@</c>
    /// before the path, so a password that itself contains an <c>@</c> (for
    /// example <c>https://user:p@ss@host/repo.git</c>) is captured in full. It
    /// cannot cross a <c>/</c> or whitespace, so a path-embedded <c>@</c> such as
    /// <c>https://host/p@th</c> is left untouched.
    /// </para>
    /// <para>
    /// scp-style remotes such as <c>git@host:path</c> have no <c>://</c> and are
    /// intentionally not matched: they carry a username but not a secret.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"(?<scheme>[A-Za-z][A-Za-z0-9+.-]*://)(?<userinfo>[^/\s]*@)", RegexOptions.CultureInvariant)]
    private static partial Regex UrlUserInfoRegex();

    /// <summary>
    /// Removes any embedded credentials from a URL (or a message containing one)
    /// by replacing the <c>scheme://userinfo@</c> user-info with
    /// <c>&lt;redacted&gt;</c>, so tokens and passwords never reach error output
    /// or logs. See <see cref="UrlUserInfoRegex"/> for the matching rules.
    /// </summary>
    /// <param name="value">The URL or message that may contain credentials.</param>
    /// <returns>The same text with any user-info component redacted.</returns>
    public static string RedactUrlUserInfo(string value)
    {
        return UrlUserInfoRegex().Replace(value, "${scheme}<redacted>@");
    }

    /// <summary>
    /// Removes any embedded credentials from a URL by dropping the
    /// <c>userinfo@</c> component entirely, leaving a clean
    /// <c>scheme://host/path</c> URL. Use this before persisting a URL so tokens
    /// and passwords never land in a lock file; git resolves authentication for
    /// the credential-free URL via its own credential helpers at clone time.
    /// See <see cref="UrlUserInfoRegex"/> for the matching rules.
    /// </summary>
    /// <param name="value">The URL that may contain credentials.</param>
    /// <returns>The same URL with any user-info component removed.</returns>
    public static string StripUserInfo(string value)
    {
        return UrlUserInfoRegex().Replace(value, "${scheme}");
    }
}
