using System.Text.RegularExpressions;

namespace Skillz.Sources;

internal static partial class OwnerRepoParser
{
    [GeneratedRegex(@"^git@[^:]+:(.+)$")]
    private static partial Regex SshUrlRegex();

    [GeneratedRegex(@"\.git$")]
    private static partial Regex TrailingGitRegex();

    [GeneratedRegex(@"^([^/]+)/([^/]+)$")]
    private static partial Regex OwnerRepoRegex();

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
