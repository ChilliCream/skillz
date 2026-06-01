using CliWrap;
using CliWrap.Buffered;

namespace Skillz.Net;

internal sealed class GitHubTokenProvider : IGitHubTokenProvider
{
    private static readonly object s_warningLock = new();
    private static bool s_warningShown;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(githubToken))
        {
            return githubToken;
        }

        var ghToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrEmpty(ghToken))
        {
            return ghToken;
        }

        EmitWarningOnce();

        try
        {
            var result = await Cli.Wrap("gh")
                .WithArguments(["auth", "token"])
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteBufferedAsync(cancellationToken)
                .ConfigureAwait(false);

            var token = result.StandardOutput.Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    internal static void ResetWarningStateForTests()
    {
        lock (s_warningLock)
        {
            s_warningShown = false;
        }
    }

    private static void EmitWarningOnce()
    {
        lock (s_warningLock)
        {
            if (s_warningShown)
            {
                return;
            }

            s_warningShown = true;
        }

        Console.Error.Write(
            "warn: GitHub API rate limit reached; reading a token via `gh auth token`.\n"
                + "      Set GITHUB_TOKEN in your environment to skip this fallback.\n");
    }
}
