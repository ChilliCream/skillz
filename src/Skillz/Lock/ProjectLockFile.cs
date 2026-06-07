using System.Text.Json.Serialization.Metadata;
using Skillz.Paths;

namespace Skillz.Locking;

internal sealed class ProjectLockFile : JsonLockFile<LocalSkillLockFile>, IProjectLockFile
{
    public const int CurrentVersion = 1;
    public const string LockFileName = "skills-lock.json";

    protected override int LatestVersion => CurrentVersion;

    protected override string Noun => "project lock file";

    protected override JsonTypeInfo<LocalSkillLockFile> TypeInfo => JsonSourceGenerationContext.Default.LocalSkillLockFile;

    protected override int VersionOf(LocalSkillLockFile file) => file.Version;

    protected override bool HasSkills(LocalSkillLockFile file) => file.Skills is not null;

    protected override bool TrailingNewline => true;

    protected override LocalSkillLockFile CreateEmpty()
        => new()
        {
            Version = CurrentVersion,
            Skills = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal)
        };

    protected override LocalSkillLockFile PrepareForWrite(LocalSkillLockFile file)
    {
        var sorted = new Dictionary<string, LocalSkillLockEntry>(StringComparer.Ordinal);
        foreach (var key in file.Skills.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sorted[key] = file.Skills[key];
        }

        return new LocalSkillLockFile { Version = file.Version, Skills = sorted };
    }

    protected override void SanitizeOnRead(LocalSkillLockFile file)
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

    private static bool IsPoisoned(string key, LocalSkillLockEntry entry)
        => HasControl(key, entry.Source, entry.SkillPath, entry.Ref)
            // A null/empty SkillPath is left untouched so existing lock entries are not
            // retroactively invalidated.
            || (!string.IsNullOrEmpty(entry.SkillPath) && SafePath.IsUnsafeStoredRelative(entry.SkillPath));

    public string GetLockPath(string? cwd = null)
        => Path.Combine(cwd ?? Directory.GetCurrentDirectory(), LockFileName);

    public Task<LocalSkillLockFile> ReadAsync(string? cwd, CancellationToken cancellationToken)
        => ReadFileAsync(GetLockPath(cwd), cancellationToken);

    public Task WriteAsync(LocalSkillLockFile lockFile, string? cwd, CancellationToken cancellationToken)
        => ReplaceAsync(GetLockPath(cwd), lockFile, cancellationToken);

    public Task AddEntryAsync(
        string skillName,
        LocalSkillLockEntry entry,
        string? cwd,
        CancellationToken cancellationToken)
        => MutateAsync(
            GetLockPath(cwd),
            lockFile =>
            {
                lockFile.Skills[skillName] = entry;
                return true;
            },
            cancellationToken);

    public async Task<bool> RemoveEntryAsync(string skillName, string? cwd, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(GetLockPath(cwd), lockFile => removed = lockFile.Skills.Remove(skillName), cancellationToken);
        return removed;
    }

    public async Task<bool> HasSkillAsync(string skillName, string? cwd, CancellationToken cancellationToken)
    {
        var lockFile = await ReadAsync(cwd, cancellationToken);
        return lockFile.Skills.ContainsKey(skillName);
    }

    public Task<string> ComputeSkillFolderHashAsync(string skillDirectory, CancellationToken cancellationToken)
        => Task.FromResult(SafeTreeHash.ComputeTreeHash(skillDirectory, cancellationToken));
}
