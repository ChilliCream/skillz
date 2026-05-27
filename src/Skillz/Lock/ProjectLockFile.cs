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

    public async Task<LocalSkillLockFile> ReadAsync(string? cwd = null, CancellationToken cancellationToken = default)
    {
        var lockPath = GetLockPath(cwd);

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.LocalSkillLockFile,
                cancellationToken).ConfigureAwait(false);

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
            return CreateEmpty();
        }
    }

    public async Task WriteAsync(LocalSkillLockFile lockFile, string? cwd = null, CancellationToken cancellationToken = default)
    {
        var lockPath = GetLockPath(cwd);
        var sorted = SortedCopy(lockFile);

        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                var existing = await ReadInternalAsync(cwd, cancellationToken).ConfigureAwait(false);
                if (existing.Version > CurrentVersion)
                {
                    Console.Error.WriteLine($"Refusing to overwrite project lock file: on-disk version v{existing.Version} is newer than this skillz (v{CurrentVersion}).");
                    return;
                }

                await WriteInternalAsync(sorted, cwd, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task AddEntryAsync(
        string skillName,
        LocalSkillLockEntry entry,
        string? cwd = null,
        CancellationToken cancellationToken = default)
    {
        var lockPath = GetLockPath(cwd);
        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                var lockFile = await ReadInternalAsync(cwd, cancellationToken).ConfigureAwait(false);
                if (lockFile.Version > CurrentVersion)
                {
                    Console.Error.WriteLine($"Refusing to modify project lock file: on-disk version v{lockFile.Version} is newer than this skillz (v{CurrentVersion}).");
                    return;
                }

                lockFile.Skills[skillName] = entry;
                await WriteInternalAsync(lockFile, cwd, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveEntryAsync(
        string skillName,
        string? cwd = null,
        CancellationToken cancellationToken = default)
    {
        var lockPath = GetLockPath(cwd);
        var removed = false;
        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                var lockFile = await ReadInternalAsync(cwd, cancellationToken).ConfigureAwait(false);
                if (lockFile.Version > CurrentVersion)
                {
                    Console.Error.WriteLine($"Refusing to modify project lock file: on-disk version v{lockFile.Version} is newer than this skillz (v{CurrentVersion}).");
                    return;
                }

                removed = lockFile.Skills.Remove(skillName);
                if (removed)
                {
                    await WriteInternalAsync(lockFile, cwd, cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return removed;
    }

    public async Task<bool> HasSkillAsync(
        string skillName,
        string? cwd = null,
        CancellationToken cancellationToken = default)
    {
        var lockFile = await ReadAsync(cwd, cancellationToken).ConfigureAwait(false);
        return lockFile.Skills.ContainsKey(skillName);
    }

    public async Task<string> ComputeSkillFolderHashAsync(
        string skillDirectory,
        CancellationToken cancellationToken = default)
    {
        var files = new List<(string RelativePath, byte[] Content)>();
        await CollectFilesAsync(skillDirectory, skillDirectory, files, cancellationToken).ConfigureAwait(false);

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
                if (string.Equals(name, ".git", StringComparison.Ordinal)
                    || string.Equals(name, "node_modules", StringComparison.Ordinal))
                {
                    continue;
                }

                await CollectFilesAsync(baseDir, entry, results, cancellationToken).ConfigureAwait(false);
            }
            else if (File.Exists(entry))
            {
                var content = await File.ReadAllBytesAsync(entry, cancellationToken).ConfigureAwait(false);
                var relative = Path.GetRelativePath(baseDir, entry).Replace('\\', '/');
                results.Add((relative, content));
            }
        }
    }

    private async Task<LocalSkillLockFile> ReadInternalAsync(string? cwd, CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.LocalSkillLockFile,
                cancellationToken).ConfigureAwait(false);

            if (parsed is null || parsed.Skills is null || parsed.Version < CurrentVersion)
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
            return CreateEmpty();
        }
    }

    private async Task WriteInternalAsync(
        LocalSkillLockFile lockFile,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(cwd);
        var sorted = SortedCopy(lockFile);

        await using var stream = File.Create(lockPath);
        await JsonSerializer.SerializeAsync(
            stream,
            sorted,
            JsonSourceGenerationContext.Default.LocalSkillLockFile,
            cancellationToken).ConfigureAwait(false);

        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static LocalSkillLockFile SortedCopy(LocalSkillLockFile lockFile)
    {
        var sorted = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal);
        foreach (var key in lockFile.Skills.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sorted[key] = lockFile.Skills[key];
        }

        return new LocalSkillLockFile
        {
            Version = lockFile.Version,
            Skills = sorted
        };
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
