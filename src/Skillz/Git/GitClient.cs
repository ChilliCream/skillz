using System.Text;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;

namespace Skillz.Git;

internal sealed class GitClient : IGitClient
{
    private const int DefaultCloneTimeoutMs = 300_000;
    private const string CloneTimeoutEnvVar = "SKILLZ_CLONE_TIMEOUT_MS";

    private static readonly string[] s_lfsConfig =
    [
        "filter.lfs.required=false",
        "filter.lfs.smudge=",
        "filter.lfs.clean=",
        "filter.lfs.process="
    ];

    public async Task<string> CloneAsync(
        string url,
        string targetDir,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var timeoutMs = GetCloneTimeoutMs();

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
        args.Add(url);
        args.Add(targetDir);

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
                .ExecuteAsync(timeoutCts.Token)
                .ConfigureAwait(false);

            return targetDir;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            await SafeCleanupAsync(targetDir).ConfigureAwait(false);
            var seconds = (int)Math.Round(timeoutMs / 1000.0);
            throw new GitCloneException(
                $"Clone timed out after {seconds}s. Common causes:\n"
                    + $"  - Large repository: raise the timeout with {CloneTimeoutEnvVar}=600000 (10m)\n"
                    + "  - Slow network: retry, or clone manually and pass the local path to 'skills add'\n"
                    + "  - Private repo without credentials: ensure auth is configured\n"
                    + "      - For SSH: ssh-add -l (to check loaded keys)\n"
                    + "      - For HTTPS: gh auth status (if using GitHub CLI)",
                url,
                isTimeout: true);
        }
        catch (CommandExecutionException)
        {
            await SafeCleanupAsync(targetDir).ConfigureAwait(false);
            var errorMessage = stdErrBuilder.ToString();
            ThrowMappedError(url, errorMessage);
            throw;
        }
    }

    public async Task<string?> GetDefaultBranchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(new[] { "ls-remote", "--symref", url, "HEAD" })
                .WithEnvironmentVariables(env => env.Set("GIT_TERMINAL_PROMPT", "0").Set("GIT_LFS_SKIP_SMUDGE", "1"))
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteBufferedAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("ref: refs/heads/", StringComparison.Ordinal))
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
        var isAuthError =
            errorMessage.Contains("Authentication failed", StringComparison.Ordinal)
            || errorMessage.Contains("could not read Username", StringComparison.Ordinal)
            || errorMessage.Contains("Permission denied", StringComparison.Ordinal)
            || errorMessage.Contains("Repository not found", StringComparison.Ordinal);

        if (isAuthError)
        {
            throw new GitCloneException(
                $"Authentication failed for {url}.\n"
                    + "  - For private repos, ensure you have access\n"
                    + "  - For SSH: Check your keys with 'ssh -T git@github.com'\n"
                    + "  - For HTTPS: Run 'gh auth login' or configure git credentials",
                url,
                isAuthError: true);
        }

        throw new GitCloneException($"Failed to clone {url}: {errorMessage.Trim()}", url);
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
