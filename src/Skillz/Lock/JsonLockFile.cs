using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Skillz.Utils;

namespace Skillz.Locking;

/// <summary>
/// Shared machinery for the JSON lock files: corruption-tolerant reads, cross-process locking,
/// version guarding, and an atomic temp-file + rename write. Subclasses supply the concrete model
/// type and the few points where the two lock files differ.
/// </summary>
internal abstract class JsonLockFile<TFile> where TFile : class
{
    /// <summary>The schema version this process writes and supports.</summary>
    protected abstract int LatestVersion { get; }

    /// <summary>Lower-case noun used in warnings and errors, e.g. <c>"global lock file"</c>.</summary>
    protected abstract string Noun { get; }

    /// <summary>Source-generated metadata used to (de)serialize <typeparamref name="TFile"/>.</summary>
    protected abstract JsonTypeInfo<TFile> TypeInfo { get; }

    protected abstract int VersionOf(TFile file);

    /// <summary>Whether a deserialized file carries the required <c>skills</c> map (vs. a null one).</summary>
    protected abstract bool HasSkills(TFile file);

    protected abstract TFile CreateEmpty();

    /// <summary>Hook to normalize a file immediately before it is serialized (sort, trim, …).</summary>
    protected virtual TFile PrepareForWrite(TFile file) => file;

    /// <summary>
    /// Hook to drop untrusted/poisoned entries from a freshly deserialized file (e.g. a skill whose
    /// key or fields carry a control byte, or whose path escapes the skill directory). A bad entry is
    /// silently skipped so it cannot block reads or mutations of its clean siblings.
    /// </summary>
    protected virtual void SanitizeOnRead(TFile file) { }

    /// <summary>
    /// Returns whether any supplied field contains a C0/DEL control byte. Such a byte in a source,
    /// ref, URL, or path is illegitimate and a CR/LF injection vector once the value is interpolated
    /// into a git ref or HTTP request. Used by the concrete files' <see cref="SanitizeOnRead"/>.
    /// </summary>
    protected static bool HasControl(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (field?.ContainsControlCharacter() == true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="skillPath"/> is structurally unsafe: rooted (an absolute or
    /// drive/UNC path), containing a <c>..</c> segment, or containing a backslash. A legitimate
    /// relative path such as <c>skills/foo/SKILL.md</c> returns <see langword="false"/>.
    /// </summary>
    protected static bool IsUnsafeSkillPath(string? skillPath)
    {
        if (string.IsNullOrEmpty(skillPath))
        {
            return false;
        }

        if (skillPath.ContainsOrdinal('\\'))
        {
            return true;
        }

        if (Path.IsPathRooted(skillPath) || skillPath.StartsWithOrdinal("/"))
        {
            return true;
        }

        foreach (var segment in skillPath.Split('/'))
        {
            if (segment.EqualsOrdinal(".."))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether to append a trailing newline after the serialized JSON.</summary>
    protected virtual bool TrailingNewline => false;

    /// <summary>
    /// Reads the lock file, returning an empty file when it is absent, corrupt, or written by an
    /// older schema; a newer file is preserved (with a warning).
    /// </summary>
    protected Task<TFile> ReadFileAsync(string lockPath, CancellationToken cancellationToken)
        => ReadCoreAsync(lockPath, throwOnCorrupt: false, cancellationToken);

    /// <summary>
    /// Under the lock, reads the current file (refusing a corrupt or newer one), applies
    /// <paramref name="mutate"/>, and atomically writes the result back when it returns <c>true</c>.
    /// </summary>
    protected Task MutateAsync(string lockPath, Func<TFile, bool> mutate, CancellationToken cancellationToken)
        => WithLockedFileAsync(
            lockPath,
            async file =>
            {
                if (mutate(file))
                {
                    await WriteAtomicAsync(lockPath, file, cancellationToken);
                }
            },
            cancellationToken);

    /// <summary>
    /// Under the lock, verifies the on-disk file is not newer, then atomically replaces it with
    /// <paramref name="newFile"/> wholesale.
    /// </summary>
    protected Task ReplaceAsync(string lockPath, TFile newFile, CancellationToken cancellationToken)
        => WithLockedFileAsync(
            lockPath,
            _ => WriteAtomicAsync(lockPath, newFile, cancellationToken),
            cancellationToken);

    private async Task WithLockedFileAsync(
        string lockPath,
        Func<TFile, Task> action,
        CancellationToken cancellationToken)
    {
        EnsureDirectory(lockPath);
        await FileLock.WithLockAsync(
            lockPath,
            async () =>
            {
                var file = await ReadCoreAsync(lockPath, throwOnCorrupt: true, cancellationToken);
                if (VersionOf(file) > LatestVersion)
                {
                    throw NewerVersionException(lockPath, VersionOf(file));
                }

                await action(file);
            },
            FileLock.DefaultTimeoutMs,
            cancellationToken);
    }

    private async Task<TFile> ReadCoreAsync(string lockPath, bool throwOnCorrupt, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(lockPath);
            var parsed = await JsonSerializer.DeserializeAsync(stream, TypeInfo, cancellationToken);

            if (parsed is null || !HasSkills(parsed))
            {
                return throwOnCorrupt ? throw CorruptException(lockPath) : CreateEmpty();
            }

            var version = VersionOf(parsed);
            if (version < LatestVersion)
            {
                return CreateEmpty();
            }

            if (version > LatestVersion && !throwOnCorrupt)
            {
                Console.Error.WriteLine(
                    $"Warning: Lock file was written by a newer version of skillz (v{version}, this is v{LatestVersion}). Data will be preserved but some fields may be ignored.");
            }

            SanitizeOnRead(parsed);
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
                throw CorruptException(lockPath);
            }

            Console.Error.WriteLine(
                $"Warning: {Capitalize(Noun)} '{lockPath}' is corrupt and will be ignored for this read.");
            return CreateEmpty();
        }
    }

    private async Task WriteAtomicAsync(string lockPath, TFile file, CancellationToken cancellationToken)
    {
        var prepared = PrepareForWrite(file);
        var tempPath = lockPath + ".tmp";
        try
        {
            await using (
                var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous)
            )
            {
                await JsonSerializer.SerializeAsync(stream, prepared, TypeInfo, cancellationToken);
                if (TrailingNewline)
                {
                    await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
                }

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

    private CliException CorruptException(string lockPath)
        => new(
            ExitCodeConstants.Failure,
            $"Refusing to modify {Noun} '{lockPath}' because it is corrupt or empty. Please repair or remove the file and retry.");

    private CliException NewerVersionException(string lockPath, int version)
        => new(
            ExitCodeConstants.Failure,
            $"Refusing to modify {Noun} '{lockPath}': on-disk version v{version} is newer than this skillz supports (v{LatestVersion}).");

    private static void EnsureDirectory(string lockPath)
    {
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
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

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
