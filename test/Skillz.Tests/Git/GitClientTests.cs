using Skillz.Git;
using Xunit;

namespace Skillz.Tests.Git;

public class GitClientTests
{
    [Fact]
    public async Task CloneAsync_Throws_GitCloneException_For_NonExistent_Local_Path()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        // Act
        var ex = await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal(fakeRepo, ex.Url);
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Cleans_Up_Target_Dir_On_Failure()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        // Act
        await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Respects_External_Cancellation()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, cts.Token)
        );
    }

    [Fact]
    public async Task GetDefaultBranchAsync_Returns_Null_For_NonExistent_Local_Path()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");

        // Act
        var result = await client.GetDefaultBranchAsync(fakeRepo, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CloneAsync_Rejects_Option_Like_Url_Target_And_Ref()
    {
        // Arrange
        var client = new GitClient();

        // Act & Assert
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
        // Act
        var ex = Assert.Throws<ArgumentException>(() =>
            GitClient.BuildCloneArguments("https://example.com/repo.git", "target", "main;rm-rf"));

        // Assert
        Assert.Equal("ref", ex.ParamName);
    }

    [Fact]
    public void CloneAsync_Inserts_DoubleDash_Before_Positional_Url()
    {
        // Act
        var args = GitClient.BuildCloneArguments("https://example.com/repo.git", "target", "main");
        var separatorIndex = args.IndexOf("--");

        // Assert
        Assert.True(separatorIndex >= 0);
        Assert.Equal("https://example.com/repo.git", args[separatorIndex + 1]);
        Assert.Equal("target", args[separatorIndex + 2]);
    }

    [Fact]
    public async Task GetDefaultBranchAsync_Rejects_Option_Like_Url()
    {
        // Arrange
        var client = new GitClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetDefaultBranchAsync("--upload-pack=sh", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GetDefaultBranchAsync_Inserts_DoubleDash_Before_Positional_Url()
    {
        // Act
        var args = GitClient.BuildLsRemoteArguments("https://example.com/repo.git");

        // Assert
        Assert.Equal("--", args[2]);
        Assert.Equal("https://example.com/repo.git", args[3]);
    }

    [Fact]
    public void RedactUrlUserInfo_Removes_Credentials_From_Messages()
    {
        // Act
        var redacted = GitClient.RedactUrlUserInfo(
            "fatal: https://user:secret@example.com/owner/repo.git failed");

        // Assert
        Assert.DoesNotContain("user:secret", redacted);
        Assert.Contains("https://<redacted>@example.com/owner/repo.git", redacted);
    }
}
