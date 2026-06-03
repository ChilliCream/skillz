using CliWrap;
using CliWrap.Buffered;

namespace Skillz.Net;

internal sealed class GitHubTokenProvider : IGitHubTokenProvider
{
    private readonly object _warningLock = new();
    private bool _warningShown;

    public async Task<string?> FindTokenAsync(CancellationToken cancellationToken)
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
                .ExecuteBufferedAsync(cancellationToken);

            var token = result.StandardOutput.Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private void EmitWarningOnce()
    {
        lock (_warningLock)
        {
            if (_warningShown)
            {
                return;
            }

            _warningShown = true;
        }

        Console.Error.Write(
            "warn: GitHub API rate limit reached; reading a token via `gh auth token`.\n"
                + "      Set GITHUB_TOKEN in your environment to skip this fallback.\n");
    }
}
