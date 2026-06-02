using Skillz.Utils;
using Xunit;

namespace Skillz.Tests.Utils;

public sealed class FileLockTests : IDisposable
{
    private readonly string _dir;
    private readonly string _target;

    public FileLockTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "skillz-filelock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _target = Path.Combine(_dir, "resource.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private string LockPath => _target + ".lock";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithLockAsync_Returns_Action_Result()
    {
        // Act
        var result = await FileLock.WithLockAsync(_target, () => Task.FromResult(42), FileLock.DefaultTimeoutMs, Ct);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WithLockAsync_Removes_Lock_File_After_Completion()
    {
        // Act
        await FileLock.WithLockAsync(_target, () => Task.FromResult(0), FileLock.DefaultTimeoutMs, Ct);

        // Assert
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task WithLockAsync_Void_Overload_Runs_Action_And_Releases()
    {
        // Arrange
        var ran = false;

        // Act
        await FileLock.WithLockAsync(
            _target,
            () =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            FileLock.DefaultTimeoutMs,
            Ct);

        // Assert
        Assert.True(ran);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task WithLockAsync_Creates_Missing_Lock_Directory()
    {
        // Arrange
        var nested = Path.Combine(_dir, "a", "b", "c", "resource.json");

        // Act
        var result = await FileLock.WithLockAsync(nested, () => Task.FromResult("ok"), FileLock.DefaultTimeoutMs, Ct);

        // Assert
        Assert.Equal("ok", result);
        Assert.True(Directory.Exists(Path.GetDirectoryName(nested)));
    }

    [Fact]
    public async Task WithLockAsync_Releases_Lock_When_Action_Throws()
    {
        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => FileLock.WithLockAsync<int>(
                _target,
                async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                },
                FileLock.DefaultTimeoutMs,
                Ct));

        // Assert — the lock was released, so the file is gone and a new acquisition succeeds.
        Assert.False(File.Exists(LockPath));
        var result = await FileLock.WithLockAsync(_target, () => Task.FromResult(7), FileLock.DefaultTimeoutMs, Ct);
        Assert.Equal(7, result);
    }

    [Fact]
    public async Task WithLockAsync_Serializes_Concurrent_Holders()
    {
        // Arrange — A acquires the lock and holds it until released.
        var aEntered = new TaskCompletionSource();
        var releaseA = new TaskCompletionSource();
        var bRan = false;

        var taskA = FileLock.WithLockAsync(
            _target,
            async () =>
            {
                aEntered.SetResult();
                await releaseA.Task;
                return 0;
            },
            timeoutMs: 30_000,
            Ct);

        await aEntered.Task;

        // Act — B tries to acquire the same lock while A holds it.
        var taskB = FileLock.WithLockAsync(
            _target,
            () =>
            {
                bRan = true;
                return Task.FromResult(0);
            },
            timeoutMs: 30_000,
            Ct);

        await Task.Delay(150, Ct);

        // Assert — B is blocked (its action has not run) for as long as A holds the lock.
        Assert.False(taskB.IsCompleted);
        Assert.False(bRan);

        releaseA.SetResult();
        await taskA;
        await taskB;

        Assert.True(bRan);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task WithLockAsync_Throws_TimeoutException_When_Lock_Unavailable()
    {
        // Arrange — occupy the lock so the file cannot be created.
        await File.WriteAllTextAsync(LockPath, "", Ct);

        // Act / Assert — a zero timeout gives up on the first failed attempt, before the staleness check.
        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => FileLock.WithLockAsync(_target, () => Task.FromResult(0), timeoutMs: 0, Ct));

        Assert.Contains(_target, ex.Message);
        Assert.Contains("Timed out acquiring lock", ex.Message);
    }

    [Fact]
    public async Task WithLockAsync_Throws_When_Cancelled_While_Waiting()
    {
        // Arrange — occupy the lock (fresh, so it is not treated as stale during the wait).
        await File.WriteAllTextAsync(LockPath, "", Ct);
        using var cts = new CancellationTokenSource();

        // Act
        var task = FileLock.WithLockAsync(_target, () => Task.FromResult(0), timeoutMs: 30_000, cts.Token);
        cts.CancelAfter(100);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task WithLockAsync_Takes_Over_Stale_Lock()
    {
        // Arrange — a lock left behind and older than the timeout is considered abandoned.
        await File.WriteAllTextAsync(LockPath, "", Ct);
        await Task.Delay(250, Ct);

        // Act — timeout (100ms) is shorter than the lock's age (~250ms), so it is reclaimed.
        var result = await FileLock.WithLockAsync(_target, () => Task.FromResult("recovered"), timeoutMs: 100, Ct);

        // Assert
        Assert.Equal("recovered", result);
        Assert.False(File.Exists(LockPath));
    }
}
