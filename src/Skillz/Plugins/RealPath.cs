namespace Skillz.Plugins;

/// <summary>
/// Resolves paths that may go through symlinks, so we know where a file
/// will <i>actually</i> end up before we write to it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Path.GetFullPath(string)"/> only tidies up a path string -
/// it removes <c>..</c> and extra slashes. It does not follow symlinks.
/// So a check like <c>"does this path start with my safe folder?"</c> can
/// be tricked by a symlink anywhere on the path.
/// </para>
/// <para>
/// This helper follows symlinks for real. It also works when the file we
/// care about does not exist yet (which is the normal case when we are
/// about to install something). The built-in .NET methods cannot do that
/// last part on their own.
/// </para>
/// </remarks>
internal static class RealPath
{
    /// <summary>
    /// Follows symlinks and returns the real path of something that
    /// already exists. Returns <see langword="null"/> if the path does
    /// not exist or cannot be resolved.
    /// </summary>
    /// <param name="path">
    /// The path to resolve. Can be relative - it will be combined with the
    /// current working directory.
    /// </param>
    /// <returns>
    /// The real path, or <see langword="null"/> if anything went wrong.
    /// Callers should treat <see langword="null"/> as "not safe."
    /// </returns>
    /// <remarks>
    /// If the path does not exist yet, use
    /// <see cref="ResolveWithNearestExistingParent(string)"/> instead.
    /// </remarks>
    public static string? TryGetRealPath(string path)
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
    /// Works out where a path will really land, even if the path does not
    /// exist yet. Follows symlinks on every parent folder that <i>does</i>
    /// exist.
    /// </summary>
    /// <param name="path">The path we want to check or write to.</param>
    /// <returns>
    /// The real path with all existing symlinked folders followed and the
    /// missing parts tacked on the end. <see langword="null"/> if it could
    /// not figure things out.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the important one for security checks. Picture installing
    /// a skill to <c>~/.agents/skills/my-skill/</c>. That folder does not
    /// exist yet - we are about to create it. So calling
    /// <see cref="TryGetRealPath(string)"/> on it returns <see langword="null"/>
    /// and the safety check silently passes. An attacker could then put a
    /// symlink somewhere up the chain (say <c>~/.agents</c> points at
    /// <c>/etc</c>) and our "safe" install ends up writing to <c>/etc</c>.
    /// </para>
    /// <para>
    /// The trick: walk up the path until we find something that <i>does</i>
    /// exist. Follow symlinks on that. Then glue the missing folder/file
    /// names back on the end. The result is the path the operating system
    /// would actually write to.
    /// </para>
    /// </remarks>
    public static string? ResolveWithNearestExistingParent(string path)
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
                var realCurrent = TryGetRealPath(current);
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
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathContainment.Comparison))
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
    /// Resolves every symlink on the path's <i>parent</i> chain but leaves the
    /// final component (the leaf) exactly as written - it is <b>not</b>
    /// followed even if it is itself a symlink.
    /// </summary>
    /// <param name="path">The path whose parent chain we want resolved.</param>
    /// <returns>
    /// The real parent directory with the original leaf name appended, or
    /// <see langword="null"/> if the parent chain could not be resolved.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the right primitive for "where will this destination live?"
    /// containment checks. We care that the <i>location</i> we are about to
    /// write to (or replace) sits inside a safe base - not where a leaf
    /// symlink currently happens to point.
    /// </para>
    /// <para>
    /// Resolving the leaf would break two legitimate cases: re-installing a
    /// skill whose agent directory is a skillz-managed symlink into the
    /// canonical store (it would look like an escape), and cleaning up a
    /// self-referential symlink. Both must be allowed; the caller deletes the
    /// leaf link safely and recreates it. A <i>parent</i> symlink that escapes
    /// the base is still caught, which is the actual attack we defend against.
    /// </para>
    /// </remarks>
    public static string? ResolveParentPreservingLeaf(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
        {
            // The path is a filesystem root; there is no parent to resolve.
            return fullPath;
        }

        var realParent = ResolveWithNearestExistingParent(parent);
        if (realParent is null)
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(realParent, Path.GetFileName(fullPath)));
    }

    /// <summary>
    /// Takes the text stored inside a symlink and turns it into a full
    /// absolute path. Does <i>not</i> follow the symlink - just builds the
    /// path string.
    /// </summary>
    /// <param name="linkPath">Where the symlink itself lives.</param>
    /// <param name="linkTarget">
    /// The text the symlink stores. Might be absolute (<c>/etc/passwd</c>)
    /// or relative (<c>../../etc/passwd</c>).
    /// </param>
    /// <returns>
    /// The absolute path the symlink points at. If the stored text is
    /// relative, it is treated as relative to the symlink's own folder,
    /// not to wherever you happen to be running from.
    /// </returns>
    /// <remarks>
    /// This is string math, not file I/O. If you want to follow the link
    /// after building the path, pass the result to
    /// <see cref="TryGetRealPath(string)"/> or
    /// <see cref="ResolveWithNearestExistingParent(string)"/>.
    /// </remarks>
    public static string ResolveSymlinkTarget(string linkPath, string linkTarget)
    {
        if (Path.IsPathRooted(linkTarget))
        {
            return Path.GetFullPath(linkTarget);
        }

        var dir = Path.GetDirectoryName(linkPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(dir, linkTarget));
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

        foreach (var part in relativePath.Split(
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
            var canonicalTarget = TryGetRealPath(resolved.FullName);
            if (canonicalTarget is null)
            {
                return null;
            }

            current = canonicalTarget;
        }

        return Path.GetFullPath(current);
    }

    /// <summary>
    /// Checks whether anything is at the given path - a real file, a real
    /// folder, or even a broken symlink whose target is gone.
    /// </summary>
    /// <remarks>
    /// A broken symlink (the link exists but what it points at is deleted)
    /// fools both <see cref="Directory.Exists(string)"/> and
    /// <see cref="File.Exists(string)"/> - they both return <c>false</c>.
    /// But the symlink itself is still there on disk. To catch that case we
    /// also peek at the path's attributes; if the "this is a symlink" flag
    /// is set, we treat the path as present.
    /// </remarks>
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
}
