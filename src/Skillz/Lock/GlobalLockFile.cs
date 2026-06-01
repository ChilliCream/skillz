using System.Collections.Immutable;
using System.Text.Json;
using Skillz.Install;
using Skillz.Utils;

namespace Skillz.Lock;

internal sealed class GlobalLockFile : IGlobalLockFile
{
    public const int CurrentVersion = 3;

    private readonly IXdgPaths _xdgPaths;
    private readonly Func<DateTime> _utcNow;

    public GlobalLockFile(IXdgPaths xdgPaths) : this(xdgPaths, () => DateTime.UtcNow) { }

    public GlobalLockFile(IXdgPaths xdgPaths, Func<DateTime> utcNow)
    {
        _xdgPaths = xdgPaths;
        _utcNow = utcNow;
    }

    public async Task<SkillLockFile> ReadAsync(CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer
                .DeserializeAsync(stream, JsonSourceGenerationContext.Default.SkillLockFile, cancellationToken)
                .ConfigureAwait(false);

            if (parsed is null || parsed.Skills is null)
            {
                return CreateEmpty();
            }

            if (parsed.Version < CurrentVersion)
            {
                return CreateEmpty();
            }

            if (parsed.Version > CurrentVersion)
            {
                Console.Error.WriteLine(
                    $"Warning: Lock file was written by a newer version of skillz (v{parsed.Version}, this is v{CurrentVersion}). Data will be preserved but some fields may be ignored.");
                return parsed;
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
            Console.Error.WriteLine($"Warning: Global lock file '{lockPath}' is corrupt and will be ignored for this read.");
            return CreateEmpty();
        }
    }

    public async Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var existing = await ReadInternalAsync(true, cancellationToken).ConfigureAwait(false);
                    if (existing.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, existing.Version);
                    }

                    await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddEntryAsync(
        string skillName,
        SkillLockEntry entry,
        CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var lockFile = await ReadInternalAsync(true, cancellationToken).ConfigureAwait(false);
                    if (lockFile.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, lockFile.Version);
                    }

                    var now = _utcNow().ToString("o");

                    var installedAt = lockFile.Skills.TryGetValue(skillName, out var existing)
                        ? existing.InstalledAt
                        : now;

                    entry.InstalledAt = installedAt;
                    entry.UpdatedAt = now;
                    lockFile.Skills[skillName] = entry;

                    await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RemoveEntryAsync(string skillName, CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var removed = false;
        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var lockFile = await ReadInternalAsync(true, cancellationToken).ConfigureAwait(false);
                    if (lockFile.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, lockFile.Version);
                    }

                    removed = lockFile.Skills.Remove(skillName);
                    if (removed)
                    {
                        await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return removed;
    }

    public async Task<SkillLockEntry?> GetEntryAsync(string skillName, CancellationToken cancellationToken = default)
    {
        var lockFile = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return lockFile.Skills.TryGetValue(skillName, out var entry) ? entry : null;
    }

    public async Task<ImmutableArray<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lockFile = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (lockFile.LastSelectedAgents is { Count: > 0 } agents)
            {
                return [.. agents];
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveLastSelectedAgentsAsync(
        ImmutableArray<string> agents,
        CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await FileLock
            .WithLockAsync(
                lockPath,
                async () =>
                {
                    var lockFile = await ReadInternalAsync(true, cancellationToken).ConfigureAwait(false);
                    if (lockFile.Version > CurrentVersion)
                    {
                        throw CreateNewerVersionException(lockPath, lockFile.Version);
                    }

                    lockFile.LastSelectedAgents = agents.ToList();
                    await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SkillLockFile> ReadInternalAsync(bool throwOnCorrupt, CancellationToken cancellationToken)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer
                .DeserializeAsync(stream, JsonSourceGenerationContext.Default.SkillLockFile, cancellationToken)
                .ConfigureAwait(false);

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

            if (parsed.Version > CurrentVersion)
            {
                if (!throwOnCorrupt)
                {
                    Console.Error.WriteLine(
                        $"Warning: Lock file was written by a newer version of skillz (v{parsed.Version}, this is v{CurrentVersion}). Data will be preserved but some fields may be ignored.");
                }
                return parsed;
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

            Console.Error.WriteLine($"Warning: Global lock file '{lockPath}' is corrupt and will be ignored for this read.");
            return CreateEmpty();
        }
    }

    private async Task WriteInternalAsync(SkillLockFile lockFile, CancellationToken cancellationToken)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();

        // Normalize: omit `dismissed` from output when empty (TS parity)
        if (lockFile.Dismissed is { Count: 0 })
        {
            lockFile.Dismissed = null;
        }

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
                    .SerializeAsync(stream, lockFile, JsonSourceGenerationContext.Default.SkillLockFile, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
            $"Refusing to modify global lock file '{lockPath}' because it is corrupt or empty. Please repair or remove the file and retry.");
    }

    private static CliException CreateNewerVersionException(string lockPath, int version)
    {
        return new CliException(
            ExitCodeConstants.Failure,
            $"Refusing to modify global lock file '{lockPath}': on-disk version v{version} is newer than this skillz supports (v{CurrentVersion}).");
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

    private static SkillLockFile CreateEmpty()
    {
        return new SkillLockFile
        {
            Version = CurrentVersion,
            Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal),
            Dismissed = null
        };
    }
}
