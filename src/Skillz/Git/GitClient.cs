using System.Text;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;

namespace Skillz.Git;

/// <summary>
/// Runs <c>git</c> as an external process to clone repositories and discover
/// default branches. Argument construction lives in <see cref="GitArguments"/> and
/// credential redaction in <see cref="GitUrl"/>; this type owns only process
/// execution, timeouts, cleanup, and error mapping.
/// </summary>
public sealed class GitClient : IGitClient
{
    private const int DefaultCloneTimeoutMs = 300_000;
    private const string CloneTimeoutEnvVar = "SKILLZ_CLONE_TIMEOUT_MS";

    /// <inheritdoc />
    public async Task<string> CloneAsync(
        string url,
        string targetDir,
        string? @ref,
        CancellationToken cancellationToken)
    {
        // Validates inputs and throws before any process is started.
        var args = GitArguments.BuildCloneArguments(url, targetDir, @ref);
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
                GitUrl.RedactUrlUserInfo(url),
                isTimeout: true);
        }
        catch (CommandExecutionException)
        {
            await SafeCleanupAsync(targetDir);
            ThrowMappedError(url, stdErrBuilder.ToString());
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetDefaultBranchAsync(string url, CancellationToken cancellationToken)
    {
        // Validates the URL and throws before the try, so an invalid URL surfaces as an
        // ArgumentException rather than being swallowed into a null result below.
        var args = GitArguments.BuildLsRemoteArguments(url);

        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(args)
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

    /// <summary>
    /// Maps a failed <c>git clone</c> to a user-facing <see cref="GitCloneException"/>,
    /// distinguishing authentication failures from other errors and redacting any
    /// credentials in the URL or git's error output. Always throws.
    /// </summary>
    private static void ThrowMappedError(string url, string errorMessage)
    {
        var redactedUrl = GitUrl.RedactUrlUserInfo(url);
        var redactedError = GitUrl.RedactUrlUserInfo(errorMessage);
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

    private static Task SafeCleanupAsync(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return Task.CompletedTask;
    }
}
