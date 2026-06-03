namespace Skillz.Utils;

internal static class FileLock
{
    private const int DefaultRetryDelayMs = 50;
    public const int DefaultTimeoutMs = 10_000;
    public const int DefaultStaleLockTimeoutMs = 60_000;

    /// <summary>
    /// Runs <paramref name="action"/> while holding an exclusive on-disk lock for
    /// <paramref name="targetPath"/>, using the default stale-lock timeout
    /// (<see cref="DefaultStaleLockTimeoutMs"/>).
    /// </summary>
    public static Task<T> WithLockAsync<T>(
        string targetPath,
        Func<Task<T>> action,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        return WithLockAsync(targetPath, action, timeoutMs, DefaultStaleLockTimeoutMs, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="action"/> while holding an exclusive on-disk lock for
    /// <paramref name="targetPath"/>. A lock whose age exceeds
    /// <paramref name="staleLockTimeoutMs"/> is treated as abandoned and reclaimed.
    /// </summary>
    public static async Task<T> WithLockAsync<T>(
        string targetPath,
        Func<Task<T>> action,
        int timeoutMs,
        int staleLockTimeoutMs,
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

            try
            {
                await using (
                    new FileStream(
                        lockPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1,
                        options: FileOptions.DeleteOnClose | FileOptions.Asynchronous)
                )
                {
                    return await action();
                }
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"Timed out acquiring lock for '{targetPath}' after {timeoutMs}ms.");
                }

                try
                {
                    var info = new FileInfo(lockPath);
                    if (info.Exists
                        && (DateTime.UtcNow - info.CreationTimeUtc).TotalMilliseconds > staleLockTimeoutMs)
                    {
                        File.Delete(lockPath);
                        continue;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                await Task.Delay(DefaultRetryDelayMs, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> while holding an exclusive on-disk lock for
    /// <paramref name="targetPath"/>, using the default stale-lock timeout
    /// (<see cref="DefaultStaleLockTimeoutMs"/>).
    /// </summary>
    public static Task WithLockAsync(
        string targetPath,
        Func<Task> action,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        return WithLockAsync(targetPath, action, timeoutMs, DefaultStaleLockTimeoutMs, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="action"/> while holding an exclusive on-disk lock for
    /// <paramref name="targetPath"/>. A lock whose age exceeds
    /// <paramref name="staleLockTimeoutMs"/> is treated as abandoned and reclaimed.
    /// </summary>
    public static Task WithLockAsync(
        string targetPath,
        Func<Task> action,
        int timeoutMs,
        int staleLockTimeoutMs,
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
            staleLockTimeoutMs,
            cancellationToken);
    }
}
