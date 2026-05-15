using System.Text.Json;
using Skillz.Install;
using Skillz.Utils;

namespace Skillz.Lock;

internal sealed class GlobalLockFile : IGlobalLockFile
{
    public const int CurrentVersion = 3;

    private readonly IXdgPaths _xdgPaths;
    private readonly Func<DateTime> _utcNow;

    public GlobalLockFile(IXdgPaths xdgPaths)
        : this(xdgPaths, () => DateTime.UtcNow)
    {
    }

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
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.SkillLockFile,
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

    public async Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                await using var stream = File.Create(lockPath);
                await JsonSerializer.SerializeAsync(
                    stream,
                    lockFile,
                    JsonSourceGenerationContext.Default.SkillLockFile,
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task AddEntryAsync(string skillName, SkillLockEntry entry, CancellationToken cancellationToken = default)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                var lockFile = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
                var now = _utcNow().ToString("o");

                var installedAt = lockFile.Skills.TryGetValue(skillName, out var existing)
                    ? existing.InstalledAt
                    : now;

                entry.InstalledAt = installedAt;
                entry.UpdatedAt = now;
                lockFile.Skills[skillName] = entry;

                await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                var lockFile = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
                removed = lockFile.Skills.Remove(skillName);
                if (removed)
                {
                    await WriteInternalAsync(lockFile, cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return removed;
    }

    public async Task<SkillLockEntry?> GetEntryAsync(string skillName, CancellationToken cancellationToken = default)
    {
        var lockFile = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return lockFile.Skills.TryGetValue(skillName, out var entry) ? entry : null;
    }

    private async Task<SkillLockFile> ReadInternalAsync(CancellationToken cancellationToken)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();

        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.SkillLockFile,
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

    private async Task WriteInternalAsync(SkillLockFile lockFile, CancellationToken cancellationToken)
    {
        var lockPath = _xdgPaths.GetGlobalLockPath();
        await using var stream = File.Create(lockPath);
        await JsonSerializer.SerializeAsync(
            stream,
            lockFile,
            JsonSourceGenerationContext.Default.SkillLockFile,
            cancellationToken).ConfigureAwait(false);
    }

    private static SkillLockFile CreateEmpty()
    {
        return new SkillLockFile
        {
            Version = CurrentVersion,
            Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal),
            Dismissed = new Dictionary<string, bool>(StringComparer.Ordinal)
        };
    }
}
