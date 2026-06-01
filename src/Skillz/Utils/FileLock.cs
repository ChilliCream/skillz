namespace Skillz.Utils;

internal static class FileLock
{
    private const int DefaultRetryDelayMs = 50;
    private const int DefaultTimeoutMs = 10_000;

    public static async Task<T> WithLockAsync<T>(
        string targetPath,
        Func<Task<T>> action,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
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
                        && (DateTime.UtcNow - info.CreationTimeUtc).TotalMilliseconds > timeoutMs)
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

    public static Task WithLockAsync(
        string targetPath,
        Func<Task> action,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
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
}
