using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;

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
    {
        var relativePaths = new List<string>();
        CollectFiles(skillDirectory, skillDirectory, relativePaths, cancellationToken);
        relativePaths.Sort(StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8192];
        foreach (var relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));

            var fullPath = Path.Combine(skillDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            using var handle = File.OpenHandle(fullPath);
            long offset = 0;
            int read;
            while ((read = RandomAccess.Read(handle, buffer, offset)) > 0)
            {
                hash.AppendData(buffer[..read]);
                offset += read;
            }
        }

        var bytes = hash.GetHashAndReset();
#if NET9_0_OR_GREATER
        return Task.FromResult(Convert.ToHexStringLower(bytes));
#else
        return Task.FromResult(Convert.ToHexString(bytes).ToLowerInvariant());
#endif
    }

    private static void CollectFiles(
        string baseDir,
        string currentDir,
        List<string> results,
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

                CollectFiles(baseDir, entry, results, cancellationToken);
            }
            else if (File.Exists(entry))
            {
                results.Add(Path.GetRelativePath(baseDir, entry).Replace('\\', '/'));
            }
        }
    }
}
