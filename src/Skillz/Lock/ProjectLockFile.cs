using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Skillz.Utils;

namespace Skillz.Lock;

internal sealed class ProjectLockFile : IProjectLockFile
{
    public const int CurrentVersion = 1;
    public const string LockFileName = "skills-lock.json";

    public string GetLockPath(string? cwd = null)
    {
        return Path.Combine(cwd ?? Directory.GetCurrentDirectory(), LockFileName);
    }

    public async Task<LocalSkillLockFile> ReadAsync(string? cwd, CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer
                .DeserializeAsync(stream, JsonSourceGenerationContext.Default.LocalSkillLockFile, cancellationToken);

            if (parsed is null || parsed.Skills is null)
            {
                return CreateEmpty();
            }

            if (parsed.Version < CurrentVersion)
            {
                return CreateEmpty();
            }

            return parsed;
        }
        catch (FileNotFoundException)
        {
            return CreateEmpty();
        }
        catch (DirectoryNotFoundException)
        {
            return CreateEmpty();
        }
        catch (JsonException)
        {
            Console.Error.WriteLine($"Warning: Project lock file '{lockPath}' is corrupt and will be ignored for this read.");
            return CreateEmpty();
        }
    }

    public async Task WriteAsync(
        LocalSkillLockFile lockFile,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);
        var sorted = SortedCopy(lockFile);

        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var existing = await ReadInternalAsync(cwd, true, cancellationToken);
                    if (existing.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, existing.Version);
                    }

                    await WriteInternalAsync(sorted, cwd, cancellationToken);
                },
                FileLock.DefaultTimeoutMs,
                cancellationToken);
    }

    public async Task AddEntryAsync(
        string skillName,
        LocalSkillLockEntry entry,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);
        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var lockFile = await ReadInternalAsync(cwd, true, cancellationToken);
                    if (lockFile.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, lockFile.Version);
                    }

                    lockFile.Skills[skillName] = entry;
                    await WriteInternalAsync(lockFile, cwd, cancellationToken);
                },
                FileLock.DefaultTimeoutMs,
                cancellationToken);
    }

    public async Task<bool> RemoveEntryAsync(
        string skillName,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);
        var removed = false;
        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var lockFile = await ReadInternalAsync(cwd, true, cancellationToken);
                    if (lockFile.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, lockFile.Version);
                    }

                    removed = lockFile.Skills.Remove(skillName);
                    if (removed)
                    {
                        await WriteInternalAsync(lockFile, cwd, cancellationToken);
                    }
                },
                FileLock.DefaultTimeoutMs,
                cancellationToken);

        return removed;
    }

    public async Task<bool> HasSkillAsync(
        string skillName,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var lockFile = await ReadAsync(cwd, cancellationToken);
        return lockFile.Skills.ContainsKey(skillName);
    }

    public async Task<string> ComputeSkillFolderHashAsync(
        string skillDirectory,
        CancellationToken cancellationToken)
    {
        var files = new List<(string RelativePath, byte[] Content)>();
        await CollectFilesAsync(skillDirectory, skillDirectory, files, cancellationToken);

        files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (relativePath, content) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData(content);
        }

        var bytes = hash.GetHashAndReset();
#if NET9_0_OR_GREATER
        return Convert.ToHexStringLower(bytes);
#else
        return Convert.ToHexString(bytes).ToLowerInvariant();
#endif
    }

    private static async Task CollectFilesAsync(
        string baseDir,
        string currentDir,
        List<(string RelativePath, byte[] Content)> results,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(currentDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);

            if (Directory.Exists(entry))
            {
                if (name.EqualsOrdinal(".git")
                    || name.EqualsOrdinal("node_modules"))
                {
                    continue;
                }

                await CollectFilesAsync(baseDir, entry, results, cancellationToken);
            }
            else if (File.Exists(entry))
            {
                var content = await File.ReadAllBytesAsync(entry, cancellationToken);
                var relative = Path.GetRelativePath(baseDir, entry).Replace('\\', '/');
                results.Add((relative, content));
            }
        }
    }

    private async Task<LocalSkillLockFile> ReadInternalAsync(
        string? cwd,
        bool throwOnCorrupt,
        CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer
                .DeserializeAsync(stream, JsonSourceGenerationContext.Default.LocalSkillLockFile, cancellationToken);

            if (parsed is null || parsed.Skills is null)
            {
                if (throwOnCorrupt)
                {
                    throw CreateCorruptLockException(lockPath);
                }

                return CreateEmpty();
            }

            if (parsed.Version < CurrentVersion)
            {
                return CreateEmpty();
            }

            return parsed;
        }
        catch (FileNotFoundException)
        {
            return CreateEmpty();
        }
        catch (DirectoryNotFoundException)
        {
            return CreateEmpty();
        }
        catch (JsonException)
        {
            if (throwOnCorrupt)
            {
                throw CreateCorruptLockException(lockPath);
            }

            Console.Error.WriteLine($"Warning: Project lock file '{lockPath}' is corrupt and will be ignored for this read.");
            return CreateEmpty();
        }
    }

    private async Task WriteInternalAsync(LocalSkillLockFile lockFile, string? cwd, CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);
        var sorted = SortedCopy(lockFile);

        var tempPath = lockPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             options: FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, sorted, JsonSourceGenerationContext.Default.LocalSkillLockFile, cancellationToken);

                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, lockPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static CliException CreateCorruptLockException(string lockPath)
    {
        return new CliException(
            ExitCodeConstants.Failure,
            $"Refusing to modify project lock file '{lockPath}' because it is corrupt or empty. Please repair or remove the file and retry.");
    }

    private static CliException CreateNewerVersionException(string lockPath, int version)
    {
        return new CliException(
            ExitCodeConstants.Failure,
            $"Refusing to modify project lock file '{lockPath}': on-disk version v{version} is newer than this skillz supports (v{CurrentVersion}).");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static LocalSkillLockFile SortedCopy(LocalSkillLockFile lockFile)
    {
        var sorted = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal);
        foreach (var key in lockFile.Skills.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sorted[key] = lockFile.Skills[key];
        }

        return new LocalSkillLockFile { Version = lockFile.Version, Skills = sorted };
    }

    private static LocalSkillLockFile CreateEmpty()
    {
        return new LocalSkillLockFile
        {
            Version = CurrentVersion,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };
    }
}
