using Microsoft.Win32.SafeHandles;

namespace Skillz.Paths;

/// <summary>
/// Centralizes every path-safety decision in skillz: containment, real-path
/// resolution, no-follow open/write, and OS-correct comparison. Callers should route
/// filesystem access through here (or <c>IFileStore</c> for untrusted paths) rather than
/// raw <c>System.IO</c>, so these decisions stay in one place.
/// </summary>
internal static class SafePath
{
    /// <summary>
    /// Ordinal on Linux (case-sensitive FS), OrdinalIgnoreCase elsewhere.
    /// </summary>
    public static StringComparison Comparison
        => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Companion comparer for sets/dictionaries keyed by path.
    /// </summary>
    public static StringComparer Comparer
        => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// OS-correct equality of two paths from the single comparison source. Compares raw
    /// strings; the caller pre-resolves both sides if "same real location" is meant.
    /// </summary>
    /// <param name="a">The first path.</param>
    /// <param name="b">The second path.</param>
    public static bool PathEquals(string a, string b) => string.Equals(a, b, Comparison);

    /// <summary>
    /// Whether <paramref name="path"/> contains a literal <c>..</c> segment under either slash style. Syntactic only.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    public static bool ContainsParentTraversalSegment(string path)
    {
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Throws <see cref="CliException"/> when <paramref name="subpath"/> has a <c>..</c> segment; returns it unchanged otherwise.
    /// </summary>
    /// <param name="subpath">The subpath to validate.</param>
    public static string ValidateNoTraversal(string subpath)
    {
        if (ContainsParentTraversalSegment(subpath))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"""Unsafe subpath: "{subpath}" contains path traversal segments. Subpaths must not contain ".." components.""");
        }

        return subpath;
    }

    /// <summary>
    /// Syntactic gate for a manifest "skills" entry: must start with <c>./</c>, no
    /// <c>..</c> segment, no control byte. Ordinal is intentional: the <c>./</c> prefix is
    /// a fixed syntactic literal, not a filesystem path comparison.
    /// </summary>
    /// <param name="path">The manifest-relative path to validate.</param>
    public static bool IsValidManifestRelativePath(string path)
    {
        if (path.ContainsControlCharacter())
        {
            return false;
        }

        return path.StartsWithOrdinal("./") && !ContainsParentTraversalSegment(path);
    }

    /// <summary>
    /// Syntactic gate for a single untrusted relative path used to build a write target
    /// (legacy well-known files; stored lock relatives): non-empty, not rooted, no
    /// backslash, no <c>..</c> segment, no control byte.
    /// </summary>
    /// <param name="relative">The relative path to validate.</param>
    public static bool IsValidStoredRelative(string? relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.ContainsControlCharacter())
        {
            return false;
        }

        if (Path.IsPathRooted(relative)
            || relative.StartsWithOrdinal("/")
            || relative.ContainsOrdinal('\\'))
        {
            return false;
        }

        return !ContainsParentTraversalSegment(relative);
    }

    /// <summary>
    /// Inverse spelling for the lock-file read path.
    /// </summary>
    /// <param name="relative">The relative path to validate.</param>
    public static bool IsUnsafeStoredRelative(string? relative) => !IsValidStoredRelative(relative);

    /// <summary>
    /// Full real path of an entry that already exists; <see langword="null"/> on
    /// missing/error. The leaf is followed through any symlink. Callers should treat
    /// <see langword="null"/> as "not safe." Use <see cref="ResolveForCreate(string)"/>
    /// when the path may not exist yet.
    /// </summary>
    /// <param name="path">The path to resolve; relative paths are combined with the current directory.</param>
    /// <example>
    /// <code>
    /// File exists: returns the absolute path
    /// var resolved = ResolveExisting("/home/user/file.txt");
    /// Result: "/home/user/file.txt"
    ///
    /// File doesn't exist: returns null
    /// var missing = ResolveExisting("/home/user/nonexistent.txt");
    /// Result: null
    ///
    /// Symlink: follows to real target
    /// var symlink = ResolveExisting("/home/user/link");
    /// Result: "/actual/target/file.txt" (the real path behind the symlink)
    /// </code>
    /// </example>
    public static string? ResolveExisting(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var info = GetExistingInfo(fullPath);
            if (info is null)
            {
                return null;
            }

            return ResolveExistingPath(fullPath);
        }
        catch
        {
            // Anything went wrong (no permission, weird path, IO error) -
            // pretend we could not resolve it so the caller refuses to trust
            // it. Better safe than sorry.
            return null;
        }
    }

    /// <summary>
    /// Resolve-before-create: the nearest existing ancestor is resolved through
    /// real symlinks and the missing tail re-appended, so a symlinked parent cannot
    /// redirect a not-yet-existing destination outside its expected base. Returns
    /// <see langword="null"/> when resolution fails.
    /// </summary>
    /// <param name="path">The path we want to check or write to.</param>
    public static string? ResolveForCreate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = fullPath;

        // Each time we step up one folder, we remember the name we just
        // stepped past so we can put it back later. It is a stack because
        // the last name we step past is the deepest one - we put it back
        // first.
        var remaining = new Stack<string>();

        while (!string.IsNullOrEmpty(current))
        {
            if (GetExistingInfo(current) is not null)
            {
                // Found a real folder/file. Follow any symlinks on it (and
                // anything above it), then put the missing names we saved
                // back on the end.
                var realCurrent = ResolveExisting(current);
                if (realCurrent is null)
                {
                    return null;
                }

                var resolved = realCurrent;
                while (remaining.Count > 0)
                {
                    resolved = Path.Combine(resolved, remaining.Pop());
                }

                return Path.GetFullPath(resolved);
            }

            // Nothing exists at this level yet. Save this folder's name and
            // step up one.
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathEquals(parent, current))
            {
                // We walked all the way up to the drive root or "/" without
                // finding anything that exists. Just return the path as it
                // is - there are no symlinks to worry about.
                return Path.GetFullPath(current);
            }

            remaining.Push(Path.GetFileName(current));
            current = parent;
        }

        return null;
    }

    /// <summary>
    /// Resolves every symlink on the path's PARENT chain but leaves the final
    /// component (the leaf) exactly as written; it is NOT followed even if it is itself a
    /// symlink. This is the right primitive for "where will this destination live?" so a
    /// skillz-managed agent-&gt;canonical leaf link or a stale self-loop is not read as an
    /// escape, while a parent symlink that escapes the base is still caught. Returns
    /// <see langword="null"/> when the parent chain cannot be resolved.
    /// </summary>
    /// <param name="path">The path whose parent chain we want resolved.</param>
    public static string? ResolveParentPreservingLeaf(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
        {
            // The path is a filesystem root; there is no parent to resolve.
            return fullPath;
        }

        var realParent = ResolveForCreate(parent);
        if (realParent is null)
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(realParent, Path.GetFileName(fullPath)));
    }

    private static string? ResolveExistingPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var current = root;
        var relativePath = Path.GetRelativePath(root, fullPath);
        if (relativePath == ".")
        {
            return Path.GetFullPath(current);
        }

        foreach (
            var part in relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);

            var info = GetExistingInfo(current);
            if (info is null)
            {
                return null;
            }

            // ReparsePoint means "this is a symlink" on Linux/macOS, or a
            // junction/mount point on Windows. Resolve every segment so a
            // symlinked parent cannot hide where the final path really lands.
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                current = Path.GetFullPath(info.FullName);
                continue;
            }

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is null)
            {
                return null;
            }

            // ResolveLinkTarget follows the link chain but can hand back a target whose own parent
            // chain still contains symlinks (e.g. on macOS a target under /var/folders, where /var
            // is itself a symlink to /private/var). Re-resolve it from the root so the result is
            // fully canonical; otherwise one path resolves to /private/var and another to /var and a
            // containment check between them spuriously fails. On Linux, where the parents hold no
            // symlinks, this is a no-op.
            var canonicalTarget = ResolveExisting(resolved.FullName);
            if (canonicalTarget is null)
            {
                return null;
            }

            current = canonicalTarget;
        }

        return Path.GetFullPath(current);
    }

    /// <summary>
    /// Checks whether anything is at <paramref name="path"/> - a real file, a real folder,
    /// or even a broken symlink whose target is gone. A broken symlink fools both
    /// <see cref="Directory.Exists(string)"/> and <see cref="File.Exists(string)"/>, so the
    /// path's attributes are also probed; the <c>(FileAttributes)(-1)</c> missing-entry
    /// sentinel is guarded so a genuinely missing path returns <see langword="null"/>.
    /// </summary>
    private static FileSystemInfo? GetExistingInfo(string path)
    {
        var directoryInfo = new DirectoryInfo(path);
        if (directoryInfo.Exists)
        {
            return directoryInfo;
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists)
        {
            return fileInfo;
        }

        // Broken symlink check: ask the OS for the path's attributes. If it
        // is a symlink whose target is gone, the "this is a symlink" flag
        // is still set even though Exists returns false.
        //
        // Careful: on .NET, FileInfo.Attributes returns (FileAttributes)(-1)
        // for a path that does not exist at all (no entry on disk, broken or
        // otherwise) WITHOUT throwing. Since -1 has every bit set, the naive
        // ReparsePoint test would treat every non-existent path as a broken
        // symlink. Guard against that so genuinely missing paths return null.
        try
        {
            var attributes = fileInfo.Attributes;
            if (attributes == (FileAttributes)(-1))
            {
                return null;
            }

            return (attributes & FileAttributes.ReparsePoint) != 0 ? fileInfo : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> resolves to a location inside (or equal to)
    /// <paramref name="root"/>, resolving real symlinks per the supplied target leaf policy.
    /// </summary>
    /// <param name="root">The base directory the target must stay within.</param>
    /// <param name="target">The path being checked.</param>
    /// <param name="targetLeaf">Leaf policy for the target.</param>
    /// <remarks>
    /// A <c>..</c> segment in the target is rejected before any resolution, so
    /// <c>link/../x</c> cannot lexically collapse past a symlink. <paramref name="root"/> is
    /// program-chosen, never attacker-controlled.
    /// </remarks>
    public static bool Contains(string root, string target, LeafPolicy targetLeaf)
    {
        // Reject '..' in the target before resolving so ordering is enforced by the
        // primitive, not by a documented caller contract.
        if (ContainsParentTraversalSegment(target))
        {
            return false;
        }

        var realRoot = ResolveForCreate(root);
        var realTarget =
            targetLeaf == LeafPolicy.Follow ? ResolveForCreate(target) : ResolveParentPreservingLeaf(target);

        return realRoot is not null && realTarget is not null && IsContainedNormalized(realTarget, realRoot);
    }

    /// <summary>
    /// Throws <see cref="CliException"/> when not contained.
    /// </summary>
    /// <param name="root">The base directory the target must stay within.</param>
    /// <param name="target">The path being checked.</param>
    /// <param name="targetLeaf">Leaf policy for the target.</param>
    /// <param name="message">Optional override for the failure message.</param>
    public static void EnsureContained(string root, string target, LeafPolicy targetLeaf, string? message = null)
    {
        if (!Contains(root, target, targetLeaf))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                message ?? $"Path '{target}' escapes its expected root '{root}'.",
                title: "Path containment violation");
        }
    }

    /// <summary>
    /// Whether the LEAF at <paramref name="path"/> is a reparse point (without following
    /// it) OR cannot be probed. Returns <see langword="true"/> when the leaf is a reparse
    /// point, or when its status cannot be determined - an ambiguous stat failure is
    /// treated as unsafe (fail CLOSED). Returns <see langword="false"/> only for a
    /// genuinely-missing leaf or a clean non-reparse leaf.
    /// </summary>
    private static bool LeafIsReparsePointOrUnprobeable(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes == (FileAttributes)(-1))
            {
                return false; // nothing there - a fresh create cannot follow a link that is not present
            }

            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false; // genuinely missing leaf: the normal write case
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Any non-"missing" stat failure is AMBIGUOUS. Do not classify it as
            // "no reparse point"; refuse.
            return true;
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> for reading WITHOUT following a symlinked leaf,
    /// after confirming it is contained in <paramref name="containRoot"/>. Used by the lock
    /// hasher and by discovery's SKILL.md read so external/secret bytes behind a symlink
    /// never enter the digest or RawContent. Throws if the leaf is a reparse point or escapes.
    /// </summary>
    /// <param name="path">The file to open for reading.</param>
    /// <param name="containRoot">The root the file must be contained in.</param>
    public static SafeFileHandle OpenReadNoFollow(string path, string containRoot)
    {
        var safe = ContainAndRefuseReparseLeaf(path, containRoot, "Refusing to read through a symlinked path");
        return File.OpenHandle(safe, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
    }

    /// <summary>
    /// Reads all text of <paramref name="path"/> WITHOUT following a symlinked leaf,
    /// after containing it in <paramref name="containRoot"/>. A symlinked SKILL.md leaf
    /// inside a contained directory is refused, not dereferenced into RawContent.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="containRoot">The root the file must be contained in.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    public static async Task<string> ReadAllTextNoFollowAsync(
        string path,
        string containRoot,
        CancellationToken cancellationToken)
    {
        using var handle = OpenReadNoFollow(path, containRoot);
        await using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> WITHOUT following a
    /// symlinked leaf, after confirming containment in <paramref name="containRoot"/>. A
    /// destination whose leaf is a symlink pointing outside the base is refused instead of
    /// written through. There is no leaf-following write primitive in this module, so a
    /// preserve-check-then-follow-write mistake cannot be expressed.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <param name="containRoot">The root the file must be contained in.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    public static async Task WriteAllBytesNoFollowAsync(
        string path,
        byte[] bytes,
        string containRoot,
        CancellationToken cancellationToken)
    {
        var safe = ContainAndRefuseReparseLeaf(path, containRoot, "Refusing to write through a symlinked path");

        // FileMode.Create truncates an existing regular file or creates a new one. We have
        // already refused an existing reparse-point leaf, so Create cannot truncate-through a
        // symlink. FileShare.None: exclusive while we write.
        await using var stream = new FileStream(
            safe,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            });

        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// The shared no-follow guard: contain the parent chain (followed) against the base,
    /// preserve the leaf, normalize away a trailing separator, lstat-probe the leaf, and
    /// refuse a reparse leaf or an ambiguous probe failure (fail CLOSED) before any open.
    /// Returns the resolved, leaf-preserved path safe to open.
    /// </summary>
    private static string ContainAndRefuseReparseLeaf(string path, string containRoot, string refusalTitle)
    {
        // Normalize a trailing directory separator FIRST. Without this, "<dir>/link/" makes
        // Path.GetFileName == "" so the symlink would be resolved as a PARENT and the reparse
        // refusal would never fire.
        var normalized = path.Length > 1 ? Path.TrimEndingDirectorySeparator(path) : path;

        // Contain the parent chain (followed) and preserve the leaf, then refuse a reparse
        // leaf below before any open.
        EnsureContained(
            containRoot,
            normalized,
            LeafPolicy.Preserve,
            message: $"{refusalTitle}: '{normalized}' escapes '{containRoot}'.");

        var resolved =
            ResolveParentPreservingLeaf(normalized)
            ?? throw new CliException(
                ExitCodeConstants.Failure,
                $"{refusalTitle}: '{normalized}' cannot be resolved.",
                title: refusalTitle);

        if (LeafIsReparsePointOrUnprobeable(resolved))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Refusing to follow a symlinked path: '{resolved}'.",
                title: refusalTitle,
                hint: "The final path component is a symlink or junction; skillz will not read or write through it.");
        }

        return resolved;
    }

    /// <summary>
    /// Display-only abbreviation: leading home -&gt; <c>~</c>, leading cwd -&gt; <c>.</c>.
    /// Lexical, gives NO resolution or containment guarantee, must never be reused as a path.
    /// Lives here so it shares the OS-correct comparison, but the name and doc make clear it
    /// is not a security check.
    /// </summary>
    /// <param name="path">The path to abbreviate.</param>
    /// <param name="home">The home directory to abbreviate to <c>~</c>, if any.</param>
    /// <param name="cwd">The current working directory to abbreviate to <c>.</c>, if any.</param>
    public static string AbbreviateForDisplay(string path, string? home, string? cwd)
    {
        if (!string.IsNullOrEmpty(home) && HasDisplayPrefix(path, home))
        {
            return "~" + path[home.Length..];
        }

        if (!string.IsNullOrEmpty(cwd) && HasDisplayPrefix(path, cwd))
        {
            return "." + (path.Length == cwd.Length ? "" : path[cwd.Length..]);
        }

        return path;

        static bool HasDisplayPrefix(string path, string basePath)
        {
            if (!path.StartsWith(basePath, Comparison))
            {
                return false;
            }

            return path.Length == basePath.Length || path[basePath.Length] == Path.DirectorySeparatorChar;
        }
    }

    internal static bool IsContainedNormalized(string normalizedTarget, string normalizedBase)
    {
        var baseWithSeparator = Path.TrimEndingDirectorySeparator(normalizedBase) + Path.DirectorySeparatorChar;

        return normalizedTarget.StartsWith(baseWithSeparator, Comparison)
            || PathEquals(
                Path.TrimEndingDirectorySeparator(normalizedTarget),
                Path.TrimEndingDirectorySeparator(normalizedBase));
    }
}
