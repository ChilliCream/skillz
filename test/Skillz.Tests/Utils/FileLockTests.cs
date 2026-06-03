using System.Diagnostics;
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
        // Arrange — an unparseable/legacy lock (no owner identity) left behind and older than the timeout
        // is treated as abandoned: there is no owner to prove alive, so age governs takeover.
        await File.WriteAllTextAsync(LockPath, "", Ct);
        await Task.Delay(250, Ct);

        // Act — timeout (100ms) is shorter than the lock's age (~250ms), so it is reclaimed.
        var result = await FileLock.WithLockAsync(_target, () => Task.FromResult("recovered"), timeoutMs: 100, Ct);

        // Assert
        Assert.Equal("recovered", result);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task WithLockAsync_Does_Not_Steal_Lock_Of_Live_Owner_Past_Timeout()
    {
        // Arrange — a lock stamped with THIS (alive) process's identity, aged well past the timeout.
        // A live owner's lock must never be stolen, regardless of age.
        await File.WriteAllTextAsync(LockPath, LiveOwnerLockContent(), Ct);
        File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow.AddMinutes(-5));

        // Act — a short timeout means the waiter gives up rather than reclaiming the live lock.
        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => FileLock.WithLockAsync(_target, () => Task.FromResult(0), timeoutMs: 300, Ct));

        // Assert — the live owner's lock survives the contention attempt.
        Assert.Contains("Timed out acquiring lock", ex.Message);
        Assert.True(File.Exists(LockPath));
    }

    [Fact]
    public async Task WithLockAsync_Recovers_Lock_When_Owner_Process_Is_Dead()
    {
        // Arrange — stamp the lock with a real-but-now-dead process's identity, aged past the timeout.
        var deadPid = await SpawnAndWaitForExitAsync();
        var nonce = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(LockPath, $"{deadPid}\n0\n{nonce}\n", Ct);
        File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow.AddSeconds(-30));

        // Act — the owner is gone, so the abandoned lock is reclaimed and the action runs.
        var result = await FileLock.WithLockAsync(_target, () => Task.FromResult("recovered"), timeoutMs: 1_000, Ct);

        // Assert
        Assert.Equal("recovered", result);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task WithLockAsync_Serializes_Concurrent_Contenders_Without_Lost_Update()
    {
        // Arrange — many tasks race to increment a shared counter under the lock. Mutual exclusion means
        // no two read-modify-write sequences interleave, so every increment is preserved.
        const int contenders = 16;
        var counter = 0;

        // Act
        var tasks = Enumerable.Range(0, contenders).Select(_ => FileLock.WithLockAsync(
            _target,
            async () =>
            {
                var observed = counter;
                await Task.Yield();
                await Task.Delay(5, Ct);
                counter = observed + 1;
                return 0;
            },
            timeoutMs: 30_000,
            Ct));

        await Task.WhenAll(tasks);

        // Assert — every contender's update landed (no lost updates from concurrent entry).
        Assert.Equal(contenders, counter);
        Assert.False(File.Exists(LockPath));
    }

    private static string LiveOwnerLockContent()
    {
        using var current = Process.GetCurrentProcess();
        var startTicks = current.StartTime.ToUniversalTime().Ticks;
        var nonce = Guid.NewGuid().ToString("N");
        return $"{current.Id}\n{startTicks}\n{nonce}\n";
    }

    private static async Task<int> SpawnAndWaitForExitAsync()
    {
        // A trivially short-lived process gives us a PID that is guaranteed not to be running afterward.
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", "exit 0" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)!;
        var pid = process.Id;
        await process.WaitForExitAsync();
        return pid;
    }
}
