using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;

namespace Skillz.Git;

/// <summary>
/// Runs <c>git</c> as an external process to clone repositories and discover
/// default branches, with credential redaction and argument-injection guards.
/// </summary>
public sealed partial class GitClient : IGitClient
{
    private const int DefaultCloneTimeoutMs = 300_000;
    private const string CloneTimeoutEnvVar = "SKILLZ_CLONE_TIMEOUT_MS";

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
    /// <item><description><c>scheme</c> — e.g. <c>https://</c> or
    /// <c>git+ssh://</c>; preserved as-is.</description></item>
    /// <item><description><c>userinfo</c> — everything up to and including the
    /// <c>@</c>; this is the part that gets hidden.</description></item>
    /// </list>
    /// <para>
    /// scp-style remotes such as <c>git@host:path</c> have no <c>://</c> and are
    /// intentionally not matched: they carry a username but not a secret.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"(?<scheme>[A-Za-z][A-Za-z0-9+.-]*://)(?<userinfo>[^/@\s]+@)", RegexOptions.CultureInvariant)]
    private static partial Regex UrlUserInfoRegex();

    private static readonly string[] s_lfsConfig =
    [
        "filter.lfs.required=false",
        "filter.lfs.smudge=",
        "filter.lfs.clean=",
        "filter.lfs.process="
    ];

    /// <inheritdoc />
    public async Task<string> CloneAsync(
        string url,
        string targetDir,
        string? @ref,
        CancellationToken cancellationToken)
    {
        ValidateNonOption(url, nameof(url));
        ValidateNonOption(targetDir, nameof(targetDir));
        if (!string.IsNullOrEmpty(@ref))
        {
            ValidateRef(@ref);
        }

        var args = BuildCloneArguments(url, targetDir, @ref);
        var timeoutMs = GetCloneTimeoutMs();

        var stdErrBuilder = new StringBuilder();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await Cli.Wrap("git")
                .WithArguments(args)
                .WithEnvironmentVariables(env => env.Set("GIT_TERMINAL_PROMPT", "0").Set("GIT_LFS_SKIP_SMUDGE", "1"))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuilder))
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteAsync(timeoutCts.Token);

            return targetDir;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            await SafeCleanupAsync(targetDir);
            var seconds = (int)Math.Round(timeoutMs / 1000.0);
            throw new GitCloneException(
                $"Clone timed out after {seconds}s. Common causes:\n"
                    + $"  - Large repository: raise the timeout with {CloneTimeoutEnvVar}=600000 (10m)\n"
                    + "  - Slow network: retry, or clone manually and pass the local path to 'skills add'\n"
                    + "  - Private repo without credentials: ensure auth is configured\n"
                    + "      - For SSH: ssh-add -l (to check loaded keys)\n"
                    + "      - For HTTPS: gh auth status (if using GitHub CLI)",
                RedactUrlUserInfo(url),
                isTimeout: true);
        }
        catch (CommandExecutionException)
        {
            await SafeCleanupAsync(targetDir);
            var errorMessage = stdErrBuilder.ToString();
            ThrowMappedError(url, errorMessage);
            throw;
        }
    }

    /// <summary>
    /// Builds the argument list for <c>git clone</c>: shallow (<c>--depth=1</c>),
    /// with LFS smudge/filters disabled, an optional <c>--branch &lt;ref&gt;</c>,
    /// and the URL and target directory placed after a <c>--</c> separator so
    /// they can never be mistaken for options. Inputs are validated first
    /// (<see cref="ValidateNonOption"/> / <see cref="ValidateRef"/>).
    /// </summary>
    /// <param name="url">The repository URL to clone.</param>
    /// <param name="targetDir">The directory to clone into.</param>
    /// <param name="ref">Optional branch or tag to check out; <see langword="null"/> for the default branch.</param>
    /// <returns>The fully-formed <c>git</c> argument list.</returns>
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

    /// <inheritdoc />
    public async Task<string?> GetDefaultBranchAsync(string url, CancellationToken cancellationToken)
    {
        ValidateNonOption(url, nameof(url));

        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(BuildLsRemoteArguments(url))
                .WithEnvironmentVariables(env => env.Set("GIT_TERMINAL_PROMPT", "0").Set("GIT_LFS_SKIP_SMUDGE", "1"))
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteBufferedAsync(cancellationToken);

            foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWithOrdinal("ref: refs/heads/"))
                {
                    continue;
                }

                var refPart = line.AsSpan("ref: refs/heads/".Length);
                var tabIndex = refPart.IndexOf('\t');
                var branch = tabIndex >= 0 ? refPart[..tabIndex].ToString() : refPart.ToString();
                return branch.Trim();
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static int GetCloneTimeoutMs()
    {
        var raw = Environment.GetEnvironmentVariable(CloneTimeoutEnvVar);
        if (string.IsNullOrEmpty(raw))
        {
            return DefaultCloneTimeoutMs;
        }

        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultCloneTimeoutMs;
    }

    private static void ThrowMappedError(string url, string errorMessage)
    {
        var redactedUrl = RedactUrlUserInfo(url);
        var redactedError = RedactUrlUserInfo(errorMessage);
        var isAuthError =
            errorMessage.ContainsOrdinal("Authentication failed")
            || errorMessage.ContainsOrdinal("could not read Username")
            || errorMessage.ContainsOrdinal("Permission denied")
            || errorMessage.ContainsOrdinal("Repository not found");

        if (isAuthError)
        {
            throw new GitCloneException(
                $"Authentication failed for {redactedUrl}.\n"
                    + "  - For private repos, ensure you have access\n"
                    + "  - For SSH: Check your keys with 'ssh -T git@github.com'\n"
                    + "  - For HTTPS: Run 'gh auth login' or configure git credentials",
                redactedUrl,
                isAuthError: true);
        }

        throw new GitCloneException($"Failed to clone {redactedUrl}: {redactedError.Trim()}", redactedUrl);
    }

    /// <summary>
    /// Builds the argument list for <c>git ls-remote --symref</c> used to
    /// discover a repository's default branch. The URL is validated and placed
    /// after a <c>--</c> separator so it cannot be parsed as an option.
    /// </summary>
    /// <param name="url">The repository URL to query.</param>
    /// <returns>The fully-formed <c>git</c> argument list.</returns>
    public static string[] BuildLsRemoteArguments(string url)
    {
        ValidateNonOption(url, nameof(url));
        return ["ls-remote", "--symref", "--", url, "HEAD"];
    }

    private static void ValidateNonOption(string value, string paramName)
    {
        if (value.StartsWith('-'))
        {
            throw new ArgumentException("Git positional arguments must not start with '-'.", paramName);
        }
    }

    private static void ValidateRef(string value)
    {
        ValidateNonOption(value, "ref");
        if (!RefRegex().IsMatch(value))
        {
            throw new ArgumentException("Git ref contains unsupported characters.", "ref");
        }
    }

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

    private static Task SafeCleanupAsync(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch { }

        return Task.CompletedTask;
    }
}
