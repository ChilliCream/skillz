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

        var ex = await Assert.ThrowsAsync<GitCloneException>(
            () => client.CloneAsync(
                fakeRepo,
                targetDir,
                @ref: null,
                TestContext.Current.CancellationToken));

        Assert.Equal(fakeRepo, ex.Url);
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Cleans_Up_Target_Dir_On_Failure()
    {
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<GitCloneException>(
            () => client.CloneAsync(
                fakeRepo,
                targetDir,
                @ref: null,
                TestContext.Current.CancellationToken));

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CloneAsync(
                fakeRepo,
                targetDir,
                @ref: null,
                cts.Token));
    }

    [Fact]
    public async Task GetDefaultBranchAsync_Returns_Null_For_NonExistent_Local_Path()
    {
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");

        var result = await client.GetDefaultBranchAsync(
            fakeRepo,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
