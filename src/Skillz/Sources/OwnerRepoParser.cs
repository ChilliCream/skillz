using System.Text.RegularExpressions;

namespace Skillz.Sources;

/// <summary>
/// Works out the short <c>owner/repo</c> name of a repository from its URL.
/// </summary>
/// <remarks>
/// A repo can be written many ways - <c>https://github.com/owner/repo</c>,
/// <c>git@github.com:owner/repo.git</c>, or just <c>owner/repo</c>. This boils all of them
/// down to the same plain <c>owner/repo</c>, which is what we save in the lock file and pass
/// to the GitHub/GitLab APIs when checking a skill for updates.
///
/// The <c>[GeneratedRegex]</c> attribute makes the compiler generate the regex code; that
/// generated code is the other half of this class, which is why the class and its regex
/// methods are <see langword="partial"/>.
/// </remarks>
internal static partial class OwnerRepoParser
{
    [GeneratedRegex(@"^git@[^:]+:(.+)$")]
    private static partial Regex SshUrlRegex();

    [GeneratedRegex(@"\.git$")]
    private static partial Regex TrailingGitRegex();

    [GeneratedRegex(@"^([^/]+)/([^/]+)$")]
    private static partial Regex OwnerRepoRegex();

    /// <summary>
    /// Pulls the <c>owner/repo</c> name out of a source's URL. For example,
    /// <c>https://github.com/owner/repo.git</c> and <c>git@github.com:owner/repo</c> both give
    /// back <c>owner/repo</c>.
    /// </summary>
    /// <param name="parsed">The source to read the URL from.</param>
    /// <returns>
    /// The <c>owner/repo</c> name, or <see langword="null"/> when there is no repo to find - a
    /// local folder, or a URL that isn't a GitHub/GitLab-style HTTP or SSH link. GitLab subgroups
    /// can make the result longer than two parts, e.g. <c>group/team/repo</c>.
    /// </returns>
    public static string? FindOwnerRepo(SkillSource parsed)
    {
        if (parsed is SkillSource.Local)
        {
            return null;
        }

        var url = parsed.Url;
        if (url is null)
        {
            return null;
        }

        var sshMatch = SshUrlRegex().Match(url);
        if (sshMatch.Success)
        {
            var path = sshMatch.Groups[1].Value;
            path = TrailingGitRegex().Replace(path, string.Empty);
            return path.ContainsOrdinal('/') ? path : null;
        }

        if (!url.StartsWithOrdinal("http://")
            && !url.StartsWithOrdinal("https://"))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var pathname = uri.AbsolutePath;
        if (pathname.StartsWith('/'))
        {
            pathname = pathname[1..];
        }

        pathname = TrailingGitRegex().Replace(pathname, string.Empty);
        return pathname.ContainsOrdinal('/') ? pathname : null;
    }

    /// <summary>
    /// Splits an <c>owner/repo</c> string into its owner and repo halves.
    /// </summary>
    /// <param name="ownerRepo">A string like <c>owner/repo</c>.</param>
    /// <param name="result">The owner and repo names, when the split succeeds.</param>
    /// <returns>
    /// <see langword="true"/> for a clean two-part <c>owner/repo</c>; <see langword="false"/>
    /// otherwise (for example, a path with extra slashes like a GitLab subgroup).
    /// </returns>
    public static bool TryParseOwnerRepo(string ownerRepo, out (string Owner, string Repo) result)
    {
        var match = OwnerRepoRegex().Match(ownerRepo);
        if (match.Success)
        {
            result = (match.Groups[1].Value, match.Groups[2].Value);
            return true;
        }

        result = default;
        return false;
    }
}
