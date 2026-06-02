using System.Text.RegularExpressions;

namespace Skillz.Git;

/// <summary>
/// Builds safe <c>git</c> argument lists for the operations <see cref="GitClient"/>
/// performs, with the input validation that guards against argument injection.
/// </summary>
internal static partial class GitArguments
{
    /// <summary>
    /// Matches a conservative allow-list for a git ref (branch or tag): one or
    /// more letters, digits, or the safe punctuation characters <c>.</c>,
    /// <c>_</c>, <c>/</c>, and <c>-</c>.
    /// </summary>
    /// <remarks>
    /// The ref is passed to <c>git clone --branch &lt;ref&gt;</c>, so it reaches
    /// the git command line. This is defence-in-depth on top of the <c>--</c>
    /// argument separator and the leading-<c>-</c> rejection in
    /// <see cref="ValidateNonOption"/>: anything outside this set (whitespace,
    /// shell metacharacters, <c>..</c>, and so on) is refused rather than risk
    /// argument injection or surprising ref interpretation. It is deliberately
    /// stricter than git's own ref naming rules — ordinary branch and tag names
    /// pass, exotic ones are rejected on purpose.
    /// </remarks>
    [GeneratedRegex("^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RefRegex();

    /// <summary>
    /// Per-invocation <c>git -c</c> overrides that disable Git LFS during clone.
    /// </summary>
    /// <remarks>
    /// Setting the smudge/clean/process filters to empty (and marking LFS not required)
    /// makes git check out the small LFS <em>pointer files</em> instead of downloading the
    /// real binaries from the LFS server. We only need the repository's text — the
    /// <c>SKILL.md</c> and skill files — so this skips slow, large, and potentially failing
    /// LFS fetches. Applied via <c>-c</c> so it affects only this command, not the user's
    /// git config; <c>GitClient.CloneAsync</c> also sets <c>GIT_LFS_SKIP_SMUDGE=1</c> as a backstop.
    /// </remarks>
    private static readonly string[] s_lfsConfig =
    [
        "filter.lfs.required=false",
        "filter.lfs.smudge=",
        "filter.lfs.clean=",
        "filter.lfs.process="
    ];

    /// <summary>
    /// Builds the argument list for <c>git clone</c>: shallow (<c>--depth=1</c>),
    /// with LFS smudge/filters disabled, an optional <c>--branch &lt;ref&gt;</c>,
    /// and the URL and target directory placed after a <c>--</c> separator so
    /// they can never be mistaken for options. Inputs are validated first
    /// (<see cref="ValidateNonOption"/> / <see cref="ValidateRef"/>).
    /// </summary>
    /// <remarks>
    /// The clone is optimized to fetch only what is needed to read a skill:
    /// <c>--depth=1</c> grabs just the latest commit (no history), <see cref="s_lfsConfig"/>
    /// avoids pulling LFS binaries, and <c>--branch</c> (when a ref is given) limits the
    /// shallow fetch to that single branch or tag. The trailing <c>--</c> is a security
    /// measure: it forces git to treat the URL and target directory as positional
    /// arguments, so a value such as <c>--upload-pack=...</c> cannot be smuggled in as an
    /// option (argument injection).
    /// </remarks>
    /// <param name="url">The repository URL to clone.</param>
    /// <param name="targetDir">The directory to clone into.</param>
    /// <param name="ref">Optional branch or tag to check out; <see langword="null"/> for the default branch.</param>
    /// <returns>The fully-formed <c>git</c> argument list.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="url"/> or <paramref name="targetDir"/> looks like an option,
    /// or <paramref name="ref"/> is outside the conservative ref allow-list.
    /// </exception>
    public static List<string> BuildCloneArguments(string url, string targetDir, string? @ref)
    {
        ValidateNonOption(url, nameof(url));
        ValidateNonOption(targetDir, nameof(targetDir));
        if (!string.IsNullOrEmpty(@ref))
        {
            ValidateRef(@ref);
        }

        var args = new List<string> { "clone" };
        foreach (var config in s_lfsConfig)
        {
            args.Add("-c");
            args.Add(config);
        }
        args.Add("--depth=1");
        if (!string.IsNullOrEmpty(@ref))
        {
            args.Add("--branch");
            args.Add(@ref);
        }
        args.Add("--");
        args.Add(url);
        args.Add(targetDir);
        return args;
    }

    /// <summary>
    /// Builds the argument list for <c>git ls-remote --symref</c> used to
    /// discover a repository's default branch. The URL is validated and placed
    /// after a <c>--</c> separator so it cannot be parsed as an option.
    /// </summary>
    /// <param name="url">The repository URL to query.</param>
    /// <returns>The fully-formed <c>git</c> argument list.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="url"/> looks like an option.</exception>
    public static string[] BuildLsRemoteArguments(string url)
    {
        ValidateNonOption(url, nameof(url));
        return ["ls-remote", "--symref", "--", url, "HEAD"];
    }

    /// <summary>Rejects a positional argument that would be parsed as an option (leading <c>-</c>).</summary>
    private static void ValidateNonOption(string value, string paramName)
    {
        if (value.StartsWith('-'))
        {
            throw new ArgumentException("Git positional arguments must not start with '-'.", paramName);
        }
    }

    /// <summary>Validates a ref against the conservative <see cref="RefRegex"/> allow-list.</summary>
    private static void ValidateRef(string value)
    {
        ValidateNonOption(value, "ref");
        if (!RefRegex().IsMatch(value))
        {
            throw new ArgumentException("Git ref contains unsupported characters.", "ref");
        }
    }
}
