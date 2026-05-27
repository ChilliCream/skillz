using Skillz.Git;

namespace Skillz.Tests.TestServices;

internal sealed class TestGitClient : IGitClient
{
    public Func<string, string, string?, string>? OnClone { get; set; }

    public Func<string, string?>? OnGetDefaultBranch { get; set; }

    public Task<string> CloneAsync(
        string url,
        string targetDir,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var result = OnClone is not null ? OnClone(url, targetDir, @ref) : @ref ?? "main";
        return Task.FromResult(result);
    }

    public Task<string?> GetDefaultBranchAsync(string url, CancellationToken cancellationToken)
    {
        var result = OnGetDefaultBranch is not null ? OnGetDefaultBranch(url) : "main";
        return Task.FromResult(result);
    }
}
