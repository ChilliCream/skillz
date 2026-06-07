using Microsoft.Win32.SafeHandles;
using Skillz;
using Skillz.Paths;
using Skillz.Utils;

namespace Skillz.Tests.TestServices;

// Symlink-modeling approach: the fake carries an explicit SYMLINK SET (Symlinks),
// mapping a normalized reparse-point path to the normalized target it points at.
// This is a pure in-memory model - no real temp dir is required - and it is what
// makes the no-follow reparse-leaf refusal and the Walk's symlink handling genuinely
// testable. The reparse-leaf refusal in OpenReadNoFollow/ReadAllTextNoFollow/
// WriteAllBytesNoFollow keys on this set (NOT on a hardcoded IsSymlink => false), so
// stage-2 gap-closure tests can assert that a symlinked leaf is refused against the
// fake. Walk applies the OnSymlink policy over the same set. Containment is checked
// against ContainRoot/containRoot using the in-memory normalized paths.
internal sealed class FakeFileStore : IFileStore
{
    public Dictionary<string, byte[]> Files { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Dirs { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Models reparse points: maps a path that is itself a symlink to the path it
    /// targets. Both keys and values are normalized via <see cref="Normalize"/>.
    /// </summary>
    public Dictionary<string, string> Symlinks { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers <paramref name="linkPath"/> as a symlink pointing at <paramref name="target"/>.
    /// </summary>
    public void AddSymlink(string linkPath, string target)
        => Symlinks[Normalize(linkPath)] = Normalize(target);

    public bool PathExists(string path)
    {
        var normalized = Normalize(path);
        return Files.ContainsKey(normalized) || Dirs.Contains(normalized) || Symlinks.ContainsKey(normalized);
    }

    public bool IsSymlink(string path) => Symlinks.ContainsKey(Normalize(path));

    public bool FileExists(string path) => Files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => Dirs.Contains(Normalize(path));

    public void CreateDirectory(string path) => AddDirectoryWithAncestors(path);

    public void DeleteDirectory(string path, bool recursive)
    {
        var normalized = Normalize(path);
        if (recursive)
        {
            var prefix = normalized + "/";
            Dirs.RemoveWhere(d => d == normalized || d.StartsWithOrdinal(prefix));
            foreach (var file in Files.Keys.ToList())
            {
                if (file == normalized || file.StartsWithOrdinal(prefix))
                {
                    Files.Remove(file);
                }
            }
        }
        else
        {
            Dirs.Remove(normalized);
        }
    }

    public void DeleteFile(string path) => Files.Remove(Normalize(path));

    public void DeletePath(string path)
    {
        if (DirectoryExists(path))
        {
            DeleteDirectory(path, recursive: true);
        }
        else if (FileExists(path))
        {
            DeleteFile(path);
        }
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var normalized = Normalize(path);
        if (!Dirs.Contains(normalized))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        var prefix = normalized + "/";
        return Dirs
            .Where(d => d.StartsWithOrdinal(prefix) && !d[prefix.Length..].Contains('/'))
            .ToList();
    }

    public bool IsDirectoryEmpty(string path)
    {
        var normalized = Normalize(path);
        if (!Dirs.Contains(normalized))
        {
            return true;
        }

        var prefix = normalized + "/";
        var hasChildDir = Dirs.Any(d => d.StartsWithOrdinal(prefix));
        var hasChildFile = Files.Keys.Any(f => f.StartsWithOrdinal(prefix));
        return !hasChildDir && !hasChildFile;
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        var normalized = Normalize(path);
        if (!Files.TryGetValue(normalized, out var bytes))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        return Task.FromResult(System.Text.Encoding.UTF8.GetString(bytes));
    }

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        WriteBytes(path, System.Text.Encoding.UTF8.GetBytes(content));
        return Task.CompletedTask;
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        WriteBytes(path, bytes);
        return Task.CompletedTask;
    }

    public SafeFileHandle OpenReadNoFollow(string path, string containRoot)
    {
        // The fake cannot mint a real OS handle, but the security-relevant behaviour
        // (refuse a reparse-point leaf, refuse an escaping path) is exercised here so
        // gap-closure tests can assert the refusal. A successful path throws
        // NotSupportedException because callers that need real bytes must use a
        // real-filesystem store; tests assert the refusal, not the handle.
        ContainAndRefuseReparseLeaf(path, containRoot);
        throw new NotSupportedException(
            "FakeFileStore models reparse-leaf refusal for OpenReadNoFollow but cannot mint a real file handle.");
    }

    public Task<string> ReadAllTextNoFollowAsync(string path, string containRoot, CancellationToken cancellationToken)
    {
        var normalized = ContainAndRefuseReparseLeaf(path, containRoot);
        if (!Files.TryGetValue(normalized, out var bytes))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        return Task.FromResult(System.Text.Encoding.UTF8.GetString(bytes));
    }

    public Task WriteAllBytesNoFollowAsync(string path, byte[] bytes, string containRoot, CancellationToken cancellationToken)
    {
        var normalized = ContainAndRefuseReparseLeaf(path, containRoot);
        Files[normalized] = bytes;

        var parent = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(parent))
        {
            AddDirectoryWithAncestors(parent);
        }

        return Task.CompletedTask;
    }

    public IEnumerable<WalkEntry> Walk(string root, WalkOptions options, CancellationToken cancellationToken)
    {
        var realRoot = Normalize(options.ContainRoot);
        var start = Normalize(root);
        if (!IsContained(start, realRoot))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Cannot walk '{root}': it is not contained in '{options.ContainRoot}'.",
                title: "Unsafe tree walk");
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { realRoot };
        return WalkCore(start, realRoot, options, visited, depth: 0, cancellationToken);
    }

    private IEnumerable<WalkEntry> WalkCore(
        string dir,
        string realRoot,
        WalkOptions options,
        HashSet<string> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth > options.MaxDepth)
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Maximum directory depth ({options.MaxDepth}) exceeded at '{dir}'. A cyclic symlink may be present.",
                title: "Unsafe tree walk");
        }

        yield return new WalkEntry(dir, dir, WalkEntryKind.Directory);

        foreach (var child in ImmediateChildren(dir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = child[(dir.Length + 1)..];
            var isReparse = Symlinks.ContainsKey(child);

            if (isReparse)
            {
                foreach (var yielded in HandleReparse(child, realRoot, options, visited, depth, cancellationToken))
                {
                    yield return yielded;
                }

                continue;
            }

            if (options.SkipNames is { } skip && skip.Contains(name))
            {
                continue;
            }

            if (!IsContained(child, realRoot) || !visited.Add(child))
            {
                continue;
            }

            if (Dirs.Contains(child))
            {
                foreach (var yielded in WalkCore(child, realRoot, options, visited, depth + 1, cancellationToken))
                {
                    yield return yielded;
                }
            }
            else
            {
                yield return new WalkEntry(child, child, WalkEntryKind.File);
            }
        }
    }

    private IEnumerable<WalkEntry> HandleReparse(
        string link,
        string realRoot,
        WalkOptions options,
        HashSet<string> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        switch (options.OnSymlink)
        {
            case OnSymlink.Skip:
                yield break;

            case OnSymlink.Refuse:
                throw new CliException(
                    ExitCodeConstants.Failure,
                    $"Refusing to follow symlink '{link}' during a no-follow tree walk.",
                    title: "Unsafe tree walk");

            case OnSymlink.FollowIfContained:
                var target = ResolveSymlink(link);
                if (target is null || !IsContained(target, realRoot) || !visited.Add(target))
                {
                    yield break;
                }

                if (Dirs.Contains(target))
                {
                    foreach (var yielded in WalkCore(link, realRoot, options, visited, depth + 1, cancellationToken))
                    {
                        yield return yielded;
                    }
                }
                else if (Files.ContainsKey(target))
                {
                    yield return new WalkEntry(link, target, WalkEntryKind.File);
                }

                yield break;

            default:
                yield break;
        }
    }

    private string ContainAndRefuseReparseLeaf(string path, string containRoot)
    {
        // Mirror SafePath.ContainAndRefuseReparseLeaf: strip a trailing separator
        // first (so "dir/link/" is treated as the link leaf, not a parent), enforce
        // containment, then refuse a reparse-point leaf before any read/write.
        var normalized = Normalize(path);
        var root = Normalize(containRoot);

        if (!IsContained(normalized, root))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Refusing to follow a path that escapes '{containRoot}': '{path}'.",
                title: "Path containment violation");
        }

        if (Symlinks.ContainsKey(normalized))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Refusing to follow a symlinked path: '{normalized}'.",
                title: "Unsafe path",
                hint: "The final path component is a symlink or junction; skillz will not read or write through it.");
        }

        return normalized;
    }

    private string? ResolveSymlink(string link)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = link;
        while (Symlinks.TryGetValue(current, out var target))
        {
            if (!seen.Add(current))
            {
                return null; // cycle
            }

            current = target;
        }

        return current;
    }

    private IEnumerable<string> ImmediateChildren(string dir)
    {
        var prefix = dir + "/";
        var children = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in Dirs)
        {
            if (d.StartsWithOrdinal(prefix) && !d[prefix.Length..].Contains('/'))
            {
                children.Add(d);
            }
        }

        foreach (var f in Files.Keys)
        {
            if (f.StartsWithOrdinal(prefix) && !f[prefix.Length..].Contains('/'))
            {
                children.Add(f);
            }
        }

        foreach (var s in Symlinks.Keys)
        {
            if (s.StartsWithOrdinal(prefix) && !s[prefix.Length..].Contains('/'))
            {
                children.Add(s);
            }
        }

        return children;
    }

    private static bool IsContained(string target, string root)
    {
        if (string.Equals(target, root, StringComparison.Ordinal))
        {
            return true;
        }

        return target.StartsWithOrdinal(root + "/");
    }

    private void WriteBytes(string path, byte[] bytes)
    {
        var normalized = Normalize(path);
        Files[normalized] = bytes;

        var parent = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(parent))
        {
            AddDirectoryWithAncestors(parent);
        }
    }

    private void AddDirectoryWithAncestors(string path)
    {
        var current = Normalize(path);
        while (!string.IsNullOrEmpty(current) && Dirs.Add(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent is null ? string.Empty : Normalize(parent);
        }
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }
}
