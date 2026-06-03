using System.Diagnostics;

namespace Skillz.Utils;

internal static class FileLock
{
    private const int DefaultRetryDelayMs = 50;
    public const int DefaultTimeoutMs = 10_000;

    /// <summary>
    /// How often a held lock refreshes its heartbeat (its <see cref="File.GetLastWriteTimeUtc(string)"/>),
    /// so that a slow-but-alive holder is not mistaken for an abandoned one. Kept well below the smallest
    /// realistic timeout so several heartbeats land inside any staleness window.
    /// </summary>
    private const int HeartbeatIntervalMs = 1_000;

    public static async Task<T> WithLockAsync<T>(
        string targetPath,
        Func<Task<T>> action,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var lockPath = targetPath + ".lock";
        var lockDir = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(lockDir))
        {
            Directory.CreateDirectory(lockDir);
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileStream? stream = null;
            try
            {
                stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                // The lock file already exists (held by someone, or abandoned). Only reclaim it if its
                // owner is provably gone; a live owner's lock is never stolen, regardless of age.
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"Timed out acquiring lock for '{targetPath}' after {timeoutMs}ms.");
                }

                TryReclaimStaleLock(lockPath, timeoutMs);
                await Task.Delay(DefaultRetryDelayMs, cancellationToken);
                continue;
            }

            // We created the lock file. Stamp our identity, then run the action while heartbeating, and
            // release the lock only if we still own it.
            var owner = LockOwner.ForCurrentProcess();
            try
            {
                await WriteOwnerAsync(stream, owner, cancellationToken);
            }
            catch
            {
                await stream.DisposeAsync();
                ReleaseIfOwned(lockPath, owner);
                throw;
            }

            await stream.DisposeAsync();

            await using var heartbeat = StartHeartbeat(lockPath, owner);
            try
            {
                return await action();
            }
            finally
            {
                ReleaseIfOwned(lockPath, owner);
            }
        }
    }

    public static Task WithLockAsync(
        string targetPath,
        Func<Task> action,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        return WithLockAsync<object?>(
            targetPath,
            async () =>
            {
                await action();
                return null;
            },
            timeoutMs,
            cancellationToken);
    }

    private static async Task WriteOwnerAsync(FileStream stream, LockOwner owner, CancellationToken cancellationToken)
    {
        var bytes = owner.Serialize();
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a timer that periodically touches the lock file's last-write time, so a long-running but
    /// alive holder keeps its lock fresh. The timer stops touching once the lock is no longer ours.
    /// </summary>
    private static Timer StartHeartbeat(string lockPath, LockOwner owner)
    {
        return new Timer(
            _ =>
            {
                try
                {
                    if (CurrentOwner(lockPath) == owner)
                    {
                        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            },
            state: null,
            dueTime: HeartbeatIntervalMs,
            period: HeartbeatIntervalMs);
    }

    /// <summary>
    /// Deletes the lock file only if it still carries our identity. Avoids the classic foot-gun where a
    /// holder deletes a successor's lock (the danger of <see cref="FileOptions.DeleteOnClose"/>).
    /// </summary>
    private static void ReleaseIfOwned(string lockPath, LockOwner owner)
    {
        try
        {
            if (CurrentOwner(lockPath) == owner)
            {
                File.Delete(lockPath);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Reclaims an existing lock only when it is safe: the recorded owner process is gone (or the file is
    /// unparseable, i.e. legacy/garbage) and the heartbeat has not been refreshed within the timeout. A
    /// live owner's lock is left untouched regardless of how old it is.
    /// </summary>
    private static void TryReclaimStaleLock(string lockPath, int timeoutMs)
    {
        try
        {
            var owner = CurrentOwner(lockPath);

            // A parseable owner that is still alive owns the lock — never steal it.
            if (owner?.IsAlive() == true)
            {
                return;
            }

            // Owner is gone (crashed) or the file is unrecognizable. Require the lock to also be stale by
            // heartbeat age, so we never race a holder that just created the file but has not yet stamped
            // it, and so that an absent file is simply retried.
            var info = new FileInfo(lockPath);
            if (!info.Exists)
            {
                return;
            }

            if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalMilliseconds > timeoutMs)
            {
                File.Delete(lockPath);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static LockOwner? CurrentOwner(string lockPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(lockPath);
            return LockOwner.TryParse(bytes);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Identity stamped into a lock file: which process holds it (PID + process start time, to survive PID
    /// reuse) and a per-acquisition nonce that distinguishes one acquisition from another by the same
    /// process. Equality of all three means "this is the very lock I acquired".
    /// </summary>
    private sealed record LockOwner(int ProcessId, long StartTimeUtcTicks, Guid Nonce)
    {
        public static LockOwner ForCurrentProcess()
        {
            using var current = Process.GetCurrentProcess();
            return new LockOwner(current.Id, ProcessStartTicks(current), Guid.NewGuid());
        }

        public byte[] Serialize()
        {
            var text = $"{ProcessId}\n{StartTimeUtcTicks}\n{Nonce:N}\n";
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        public static LockOwner? TryParse(byte[] bytes)
        {
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                var parts = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3
                    || !int.TryParse(parts[0], out var processId)
                    || !long.TryParse(parts[1], out var startTicks)
                    || !Guid.TryParseExact(parts[2], "N", out var nonce))
                {
                    return null;
                }

                return new LockOwner(processId, startTicks, nonce);
            }
            catch (ArgumentException)
            {
                // Invalid UTF-8 or similar — treat as unparseable.
                return null;
            }
        }

        /// <summary>
        /// Whether the owning process is currently running. PID reuse is guarded by comparing the recorded
        /// start time against the live process's start time; a mismatch means a different process now holds
        /// that PID, so the original owner is gone.
        /// </summary>
        public bool IsAlive()
        {
            try
            {
                using var process = Process.GetProcessById(ProcessId);
                if (process.HasExited)
                {
                    return false;
                }

                // A start time of 0 means we could not read it when stamping; fall back to PID-only liveness.
                return StartTimeUtcTicks == 0 || ProcessStartTicks(process) == StartTimeUtcTicks;
            }
            catch (ArgumentException)
            {
                // No process with this id is running.
                return false;
            }
            catch (InvalidOperationException)
            {
                // Process has exited between lookup and inspection.
                return false;
            }
        }

        private static long ProcessStartTicks(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Start time is not always readable (permissions / platform); 0 disables the PID-reuse guard.
                return 0;
            }
        }
    }
}
