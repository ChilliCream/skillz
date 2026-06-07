using System.Buffers;
using System.Text.RegularExpressions;
using Skillz.Paths;
using Skillz.Skills;

namespace Skillz.Sources;

internal interface ISourceParser
{
    SkillSource Parse(string input);
}

internal sealed partial class SourceParser : ISourceParser
{
    [GeneratedRegex(@"^[a-zA-Z]:[/\\]")]
    private static partial Regex WindowsDrivePathRegex();

    [GeneratedRegex(@"^/[^/]+/[^/]+(?:\.git)?(?:/tree/[^/]+(?:/.*)?)?/?$")]
    private static partial Regex GitHubPathnameRegex();

    [GeneratedRegex(@"^/.+?/[^/]+(?:\.git)?(?:/-/tree/[^/]+(?:/.*)?)?/?$")]
    private static partial Regex GitLabPathnameRegex();

    [GeneratedRegex(@"^https?://.+\.git(?:$|[/?])", RegexOptions.IgnoreCase)]
    private static partial Regex GenericGitUrlRegex();

    [GeneratedRegex(@"^([^/]+)/([^/]+)(?:/(.+)|@(.+))?$")]
    private static partial Regex OwnerRepoOrAtRegex();

    [GeneratedRegex(@"^github:(.+)$")]
    private static partial Regex GitHubPrefixRegex();

    [GeneratedRegex(@"^gitlab:(.+)$")]
    private static partial Regex GitLabPrefixRegex();

    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)/tree/([^/]+)/(.+)")]
    private static partial Regex GitHubTreeWithPathRegex();

    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)/tree/([^/]+)$")]
    private static partial Regex GitHubTreeRegex();

    [GeneratedRegex(@"(?:^|://|@)github\.com/([^/]+)/([^/]+)")]
    private static partial Regex GitHubRepoRegex();

    [GeneratedRegex(@"^(https?)://([^/]+)/(.+?)/-/tree/([^/]+)/(.+)")]
    private static partial Regex GitLabTreeWithPathRegex();

    [GeneratedRegex(@"^(https?)://([^/]+)/(.+?)/-/tree/([^/]+)$")]
    private static partial Regex GitLabTreeRegex();

    [GeneratedRegex(@"(?:^|://|@)gitlab\.com/(.+?)(?:\.git)?/?$")]
    private static partial Regex GitLabRepoRegex();

    [GeneratedRegex(@"^([^/]+)/([^/@]+)@(.+)$")]
    private static partial Regex AtSkillRegex();

    [GeneratedRegex(@"^([^/]+)/([^/]+)(?:/(.+?))?/?$")]
    private static partial Regex ShorthandRegex();

    [GeneratedRegex(@"\.git$")]
    private static partial Regex TrailingGitRegex();

    public SkillSource Parse(string input)
    {
        if (input.ContainsControlCharacter())
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                "Invalid source: contains a disallowed control character. A source must be a repository URL, owner/repo, or a local path.");
        }

        if (IsLocalPath(input))
        {
            var resolvedPath = Path.GetFullPath(input);
            return new SkillSource.Local(resolvedPath, resolvedPath);
        }

        var fragment = ParseFragmentRef(input);
        var fragmentRef = fragment.Ref;
        var fragmentSkillFilter = fragment.SkillFilter;
        input = fragment.InputWithoutFragment;

        // The repository portion (everything before any leading '#fragment') must not be empty/blank,
        // otherwise we would fall through to a git clone of an empty/whitespace source.
        var hashIndex = input.IndexOfOrdinal('#');
        var repositoryPortion = hashIndex >= 0 ? input[..hashIndex] : input;
        if (string.IsNullOrWhiteSpace(repositoryPortion))
        {
            throw new CliException(ExitCodeConstants.Failure, "Missing required argument: source");
        }

        var githubPrefixMatch = GitHubPrefixRegex().Match(input);
        if (githubPrefixMatch.Success)
        {
            return Parse(AppendFragmentRef(githubPrefixMatch.Groups[1].Value, fragmentRef, fragmentSkillFilter));
        }

        var gitlabPrefixMatch = GitLabPrefixRegex().Match(input);
        if (gitlabPrefixMatch.Success)
        {
            return Parse(
                AppendFragmentRef(
                    $"https://gitlab.com/{gitlabPrefixMatch.Groups[1].Value}",
                    fragmentRef,
                    fragmentSkillFilter));
        }

        var githubTreeWithPathMatch = GitHubTreeWithPathRegex().Match(input);
        if (githubTreeWithPathMatch.Success)
        {
            var owner = githubTreeWithPathMatch.Groups[1].Value;
            var repo = TrailingGitRegex().Replace(githubTreeWithPathMatch.Groups[2].Value, string.Empty);
            var refValue = githubTreeWithPathMatch.Groups[3].Value;
            var subpath = githubTreeWithPathMatch.Groups[4].Value;
            return new SkillSource.GitHub(
                Url: $"https://github.com/{owner}/{repo}.git",
                Ref: !string.IsNullOrEmpty(refValue) ? refValue : fragmentRef,
                Subpath: !string.IsNullOrEmpty(subpath) ? SafePath.ValidateNoTraversal(subpath) : subpath);
        }

        var githubTreeMatch = GitHubTreeRegex().Match(input);
        if (githubTreeMatch.Success)
        {
            var owner = githubTreeMatch.Groups[1].Value;
            var repo = TrailingGitRegex().Replace(githubTreeMatch.Groups[2].Value, string.Empty);
            var refValue = githubTreeMatch.Groups[3].Value;
            return new SkillSource.GitHub(
                Url: $"https://github.com/{owner}/{repo}.git",
                Ref: !string.IsNullOrEmpty(refValue) ? refValue : fragmentRef);
        }

        var githubRepoMatch = GitHubRepoRegex().Match(input);
        if (githubRepoMatch.Success)
        {
            var owner = githubRepoMatch.Groups[1].Value;
            var repo = githubRepoMatch.Groups[2].Value;
            var cleanRepo = TrailingGitRegex().Replace(repo, string.Empty);
            return new SkillSource.GitHub(Url: $"https://github.com/{owner}/{cleanRepo}.git", Ref: fragmentRef);
        }

        var gitlabTreeWithPathMatch = GitLabTreeWithPathRegex().Match(input);
        if (gitlabTreeWithPathMatch.Success)
        {
            var protocol = gitlabTreeWithPathMatch.Groups[1].Value;
            var hostname = gitlabTreeWithPathMatch.Groups[2].Value;
            var repoPath = gitlabTreeWithPathMatch.Groups[3].Value;
            var refValue = gitlabTreeWithPathMatch.Groups[4].Value;
            var subpath = gitlabTreeWithPathMatch.Groups[5].Value;
            if (hostname != "github.com" && !string.IsNullOrEmpty(repoPath))
            {
                return new SkillSource.GitLab(
                    Url: $"{protocol}://{hostname}/{TrailingGitRegex().Replace(repoPath, string.Empty)}.git",
                    Ref: !string.IsNullOrEmpty(refValue) ? refValue : fragmentRef,
                    Subpath: !string.IsNullOrEmpty(subpath) ? SafePath.ValidateNoTraversal(subpath) : subpath);
            }
        }

        var gitlabTreeMatch = GitLabTreeRegex().Match(input);
        if (gitlabTreeMatch.Success)
        {
            var protocol = gitlabTreeMatch.Groups[1].Value;
            var hostname = gitlabTreeMatch.Groups[2].Value;
            var repoPath = gitlabTreeMatch.Groups[3].Value;
            var refValue = gitlabTreeMatch.Groups[4].Value;
            if (hostname != "github.com" && !string.IsNullOrEmpty(repoPath))
            {
                return new SkillSource.GitLab(
                    Url: $"{protocol}://{hostname}/{TrailingGitRegex().Replace(repoPath, string.Empty)}.git",
                    Ref: !string.IsNullOrEmpty(refValue) ? refValue : fragmentRef);
            }
        }

        var gitlabRepoMatch = GitLabRepoRegex().Match(input);
        if (gitlabRepoMatch.Success)
        {
            var repoPath = gitlabRepoMatch.Groups[1].Value;
            if (repoPath.ContainsOrdinal('/'))
            {
                return new SkillSource.GitLab(Url: $"https://gitlab.com/{repoPath}.git", Ref: fragmentRef);
            }
        }

        var atSkillMatch = AtSkillRegex().Match(input);
        if (atSkillMatch.Success
            && !input.ContainsOrdinal(':')
            && !input.StartsWith('.')
            && !input.StartsWith('/'))
        {
            var owner = atSkillMatch.Groups[1].Value;
            var repo = atSkillMatch.Groups[2].Value;
            var skillFilter = atSkillMatch.Groups[3].Value;
            return new SkillSource.GitHub(
                Url: $"https://github.com/{owner}/{repo}.git",
                Ref: fragmentRef,
                SkillFilter: !string.IsNullOrEmpty(fragmentSkillFilter) ? fragmentSkillFilter : skillFilter);
        }

        var shorthandMatch = ShorthandRegex().Match(input);
        if (shorthandMatch.Success
            && !input.ContainsOrdinal(':')
            && !input.StartsWith('.')
            && !input.StartsWith('/'))
        {
            var owner = shorthandMatch.Groups[1].Value;
            var repo = shorthandMatch.Groups[2].Value;
            var subpath = shorthandMatch.Groups[3].Success ? shorthandMatch.Groups[3].Value : null;
            return new SkillSource.GitHub(
                Url: $"https://github.com/{owner}/{repo}.git",
                Ref: fragmentRef,
                Subpath: !string.IsNullOrEmpty(subpath) ? SafePath.ValidateNoTraversal(subpath) : null,
                SkillFilter: !string.IsNullOrEmpty(fragmentSkillFilter) ? fragmentSkillFilter : null);
        }

        if (IsWellKnownUrl(input))
        {
            return new SkillSource.WellKnown(input);
        }

        ValidateGitTransport(input);
        return new SkillSource.Git(input, fragmentRef);
    }

    // Allowed URL schemes for the generic git transport. `file` and any "word::" transport helper
    // (e.g. ext::/fd::) are excluded because they can execute arbitrary local commands.
    private static readonly string[] s_allowedGitSchemes = ["http", "https", "ssh", "git", "git+ssh"];

    // Allow-list for the host of an scp-style (`user@host:path`) source: letters, digits, dot, and
    // hyphen. Anything else (e.g. the '=' in `git@-oProxyCommand=x`) is rejected, and a leading '-'
    // is blocked separately so the host can never be parsed as a git option.
    private static readonly SearchValues<char> s_scpHostAllowedChars = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-");

    private static void ValidateGitTransport(string input)
    {
        var schemeIndex = input.IndexOfOrdinal("://");
        if (schemeIndex >= 0)
        {
            var scheme = input[..schemeIndex];
            if (!s_allowedGitSchemes.Any(allowed => scheme.EqualsOrdinalIgnoreCase(allowed)))
            {
                throw new CliException(
                    ExitCodeConstants.Failure,
                    $"Unsupported git transport: \"{scheme}\" is not an allowed scheme.");
            }

            return;
        }

        var colonIndex = input.IndexOfOrdinal(':');
        if (colonIndex >= 0)
        {
            var beforeColon = input[..colonIndex];
            if (beforeColon.ContainsOrdinal('@'))
            {
                var atIndex = beforeColon.IndexOfOrdinal('@');
                var host = beforeColon[(atIndex + 1)..];
                if (host.Length == 0
                    || host.StartsWith('-')
                    || host.AsSpan().ContainsAnyExcept(s_scpHostAllowedChars))
                {
                    throw new CliException(
                        ExitCodeConstants.Failure,
                        $"Unsupported git transport: \"{input}\" has an invalid host.");
                }

                return;
            }
        }

        throw new CliException(ExitCodeConstants.Failure, $"Unsupported git transport: \"{input}\".");
    }

    private static bool IsLocalPath(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        if (input.StartsWithOrdinal("./")
            || input.StartsWithOrdinal("../")
            || input == "."
            || input == "..")
        {
            return true;
        }

        if (WindowsDrivePathRegex().IsMatch(input))
        {
            return true;
        }

        if (input.StartsWith('/'))
        {
            return true;
        }

        return false;
    }

    private static bool IsWellKnownUrl(string input)
    {
        if (!input.StartsWithOrdinal("http://") && !input.StartsWithOrdinal("https://"))
        {
            return false;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var hostname = uri.Host;
        if (hostname is "github.com" or "gitlab.com" or "raw.githubusercontent.com")
        {
            return false;
        }

        if (input.EndsWithOrdinal(".git"))
        {
            return false;
        }

        return true;
    }

    private readonly record struct FragmentRefResult(string InputWithoutFragment, string? Ref, string? SkillFilter);

    private static FragmentRefResult ParseFragmentRef(string input)
    {
        var hashIndex = input.IndexOfOrdinal('#');
        if (hashIndex < 0)
        {
            return new FragmentRefResult(input, null, null);
        }

        var inputWithoutFragment = input[..hashIndex];
        var fragment = input[(hashIndex + 1)..];

        if (string.IsNullOrEmpty(fragment) || !LooksLikeGitSource(inputWithoutFragment))
        {
            return new FragmentRefResult(input, null, null);
        }

        var atIndex = fragment.IndexOfOrdinal('@');
        if (atIndex == -1)
        {
            return new FragmentRefResult(inputWithoutFragment, DecodeFragmentValue(fragment), null);
        }

        var refValue = fragment[..atIndex];
        var skillFilter = fragment[(atIndex + 1)..];
        return new FragmentRefResult(
            inputWithoutFragment,
            !string.IsNullOrEmpty(refValue) ? DecodeFragmentValue(refValue) : null,
            !string.IsNullOrEmpty(skillFilter) ? DecodeFragmentValue(skillFilter) : null);
    }

    private static string DecodeFragmentValue(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return value;
        }
    }

    private static bool LooksLikeGitSource(string input)
    {
        if (input.StartsWithOrdinal("github:")
            || input.StartsWithOrdinal("gitlab:")
            || input.StartsWithOrdinal("git@"))
        {
            return true;
        }

        if (input.StartsWithOrdinal("http://") || input.StartsWithOrdinal("https://"))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var parsed))
            {
                var pathname = parsed.AbsolutePath;
                if (parsed.Host == "github.com")
                {
                    return GitHubPathnameRegex().IsMatch(pathname);
                }

                if (parsed.Host == "gitlab.com")
                {
                    return GitLabPathnameRegex().IsMatch(pathname);
                }
            }
        }

        if (GenericGitUrlRegex().IsMatch(input))
        {
            return true;
        }

        return !input.ContainsOrdinal(':')
            && !input.StartsWith('.')
            && !input.StartsWith('/')
            && OwnerRepoOrAtRegex().IsMatch(input);
    }

    private static string AppendFragmentRef(string input, string? refValue, string? skillFilter)
    {
        if (string.IsNullOrEmpty(refValue))
        {
            return input;
        }

        return !string.IsNullOrEmpty(skillFilter) ? $"{input}#{refValue}@{skillFilter}" : $"{input}#{refValue}";
    }
}
