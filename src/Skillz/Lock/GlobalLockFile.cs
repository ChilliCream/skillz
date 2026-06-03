using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;
using Skillz.Install;

namespace Skillz.Locking;

internal sealed class GlobalLockFile(XdgPaths xdgPaths, TimeProvider timeProvider)
    : JsonLockFile<SkillLockFile>
    , IGlobalLockFile
{
    public const int CurrentVersion = 3;

    protected override int LatestVersion => CurrentVersion;

    protected override string Noun => "global lock file";

    protected override JsonTypeInfo<SkillLockFile> TypeInfo => JsonSourceGenerationContext.Default.SkillLockFile;

    protected override int VersionOf(SkillLockFile file) => file.Version;

    protected override bool HasSkills(SkillLockFile file) => file.Skills is not null;

    protected override SkillLockFile CreateEmpty()
        => new()
        {
            Version = CurrentVersion,
            Skills = new Dictionary<string, SkillLockEntry>(StringComparer.Ordinal),
            Dismissed = null
        };

    protected override SkillLockFile PrepareForWrite(SkillLockFile file)
    {
        // Omit `dismissed` from output when empty.
        if (file.Dismissed is { Count: 0 })
        {
            file.Dismissed = null;
        }

        return file;
    }

    protected override void SanitizeOnRead(SkillLockFile file)
    {
        var poisoned = file.Skills
            .Where(pair => IsPoisoned(pair.Key, pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in poisoned)
        {
            file.Skills.Remove(key);
        }
    }

    private static bool IsPoisoned(string key, SkillLockEntry entry)
        => HasControl(key, entry.Source, entry.SkillPath, entry.Ref, entry.SourceUrl)
            || IsUnsafeSkillPath(entry.SkillPath);

    public Task<SkillLockFile> ReadAsync(CancellationToken cancellationToken)
        => ReadFileAsync(xdgPaths.GetGlobalLockPath(), cancellationToken);

    public Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken)
        => ReplaceAsync(xdgPaths.GetGlobalLockPath(), lockFile, cancellationToken);

    public Task AddEntryAsync(string skillName, SkillLockEntry entry, CancellationToken cancellationToken)
        => MutateAsync(
            xdgPaths.GetGlobalLockPath(),
            lockFile =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime.ToString("o");
                entry.InstalledAt = lockFile.Skills.TryGetValue(skillName, out var existing)
                    ? existing.InstalledAt
                    : now;
                entry.UpdatedAt = now;
                lockFile.Skills[skillName] = entry;
                return true;
            },
            cancellationToken);

    public async Task<bool> RemoveEntryAsync(string skillName, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(
            xdgPaths.GetGlobalLockPath(),
            lockFile => removed = lockFile.Skills.Remove(skillName),
            cancellationToken);
        return removed;
    }

    public async Task<SkillLockEntry?> FindEntryAsync(string skillName, CancellationToken cancellationToken)
    {
        var lockFile = await ReadAsync(cancellationToken);
        return lockFile.Skills.TryGetValue(skillName, out var entry) ? entry : null;
    }

    public async Task<ImmutableArray<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var lockFile = await ReadAsync(cancellationToken);
            return lockFile.LastSelectedAgents is { Count: > 0 } agents ? [.. agents] : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public Task SaveLastSelectedAgentsAsync(ImmutableArray<string> agents, CancellationToken cancellationToken)
        => MutateAsync(
            xdgPaths.GetGlobalLockPath(),
            lockFile =>
            {
                lockFile.LastSelectedAgents = agents.ToList();
                return true;
            },
            cancellationToken);
}
