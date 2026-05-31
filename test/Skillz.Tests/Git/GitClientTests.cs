using Skillz.Git;
using Xunit;

namespace Skillz.Tests.Git;

public class GitClientTests
{
    [Fact]
    public async Task CloneAsync_Throws_GitCloneException_For_NonExistent_Local_Path()
    {
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        var ex = await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, TestContext.Current.CancellationToken)
        );

        Assert.Equal(fakeRepo, ex.Url);
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Cleans_Up_Target_Dir_On_Failure()
    {
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, TestContext.Current.CancellationToken)
        );

        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Respects_External_Cancellation()
    {
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, cts.Token)
        );
    }

    [Fact]
    public async Task GetDefaultBranchAsync_Returns_Null_For_NonExistent_Local_Path()
    {
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");

        var result = await client.GetDefaultBranchAsync(fakeRepo, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CloneAsync_Rejects_Option_Like_Url_Target_And_Ref()
    {
        var client = new GitClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CloneAsync("--upload-pack=sh", "target", @ref: null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CloneAsync("https://example.com/repo.git", "--target", @ref: null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CloneAsync("https://example.com/repo.git", "target", @ref: "-main", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CloneAsync_Rejects_Refs_Outside_Conservative_Allowlist()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            GitClient.BuildCloneArguments("https://example.com/repo.git", "target", "main;rm-rf"));

        Assert.Equal("ref", ex.ParamName);
    }

    [Fact]
    public void CloneAsync_Inserts_DoubleDash_Before_Positional_Url()
    {
        var args = GitClient.BuildCloneArguments("https://example.com/repo.git", "target", "main");
        var separatorIndex = args.IndexOf("--");

        Assert.True(separatorIndex >= 0);
        Assert.Equal("https://example.com/repo.git", args[separatorIndex + 1]);
        Assert.Equal("target", args[separatorIndex + 2]);
    }

    [Fact]
    public async Task GetDefaultBranchAsync_Rejects_Option_Like_Url()
    {
        var client = new GitClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetDefaultBranchAsync("--upload-pack=sh", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GetDefaultBranchAsync_Inserts_DoubleDash_Before_Positional_Url()
    {
        var args = GitClient.BuildLsRemoteArguments("https://example.com/repo.git");

        Assert.Equal("--", args[2]);
        Assert.Equal("https://example.com/repo.git", args[3]);
    }

    [Fact]
    public void RedactUrlUserInfo_Removes_Credentials_From_Messages()
    {
        var redacted = GitClient.RedactUrlUserInfo(
            "fatal: https://user:secret@example.com/owner/repo.git failed");

        Assert.DoesNotContain("user:secret", redacted);
        Assert.Contains("https://<redacted>@example.com/owner/repo.git", redacted);
    }
}
