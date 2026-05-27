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
                    var existing = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
                    if (existing.Version > CurrentVersion)
                    {
                        Console.Error.WriteLine(
                            $"Refusing to overwrite global lock file: on-disk version v{existing.Version} is newer than this skillz (v{CurrentVersion}).");
                        return;
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
                    var lockFile = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
                    if (lockFile.Version > CurrentVersion)
                    {
                        return;
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
                    var lockFile = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
                    if (lockFile.Version > CurrentVersion)
                    {
                        removed = false;
                        return;
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

    public async Task<IReadOnlyList<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lockFile = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (lockFile.LastSelectedAgents is { Count: > 0 } agents)
            {
                return agents;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveLastSelectedAgentsAsync(
        IReadOnlyList<string> agents,
        CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            await FileLock
                .WithLockAsync(
                    lockPath,
                    async () =>
                    {
                        var lockFile = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
                        if (lockFile.Version > CurrentVersion)
                        {
                            return;
                        }

                        lockFile.LastSelectedAgents = agents.ToList();
                        await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Silently swallow — corrupted lock must not break add
        }
    }

    private async Task<SkillLockFile> ReadInternalAsync(CancellationToken cancellationToken)
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

        await using var stream = File.Create(lockPath);
        await JsonSerializer
            .SerializeAsync(stream, lockFile, JsonSourceGenerationContext.Default.SkillLockFile, cancellationToken)
            .ConfigureAwait(false);
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
