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

    public static string? GetOwnerRepo(ParsedSource parsed)
    {
        if (parsed is ParsedSource.Local)
        {
            return null;
        }

        var url = parsed switch
        {
            ParsedSource.GitHub g => g.Url,
            ParsedSource.GitLab g => g.Url,
            ParsedSource.Git g => g.Url,
            ParsedSource.WellKnown w => w.Url,
            _ => null,
        };

        if (url is null)
        {
            return null;
        }

        var sshMatch = SshUrlRegex().Match(url);
        if (sshMatch.Success)
        {
            var path = sshMatch.Groups[1].Value;
            path = TrailingGitRegex().Replace(path, string.Empty);
            return path.Contains('/', StringComparison.Ordinal) ? path : null;
        }

        if (!url.StartsWith("http://", StringComparison.Ordinal)
            && !url.StartsWith("https://", StringComparison.Ordinal))
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
        return pathname.Contains('/', StringComparison.Ordinal) ? pathname : null;
    }

    public static (string Owner, string Repo)? ParseOwnerRepo(string ownerRepo)
    {
        var match = OwnerRepoRegex().Match(ownerRepo);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : null;
    }
}
