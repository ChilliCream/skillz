namespace Skillz.Paths;

internal static class SafeTreeWalker
{
    /// <summary>
    /// Enumerates a tree under <paramref name="root"/> with one uniform symlink policy.
    /// Every yielded entry's real path is contained in <see cref="WalkOptions.ContainRoot"/>;
    /// recursion is depth-bounded; real paths are de-duplicated via a visited-set so a cycle
    /// or duplicate-real-path link cannot be processed twice.
    /// </summary>
    /// <param name="root">The directory to walk.</param>
    /// <param name="options">The walk options, including the containment root and symlink policy.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <remarks>
    /// The visited-set is a member of the walk, not a caller concern. Directories are yielded
    /// before their children. SkipNames is applied AFTER the reparse decision so an
    /// <see cref="OnSymlink.Refuse"/> walk genuinely refuses every in-tree reparse point even
    /// if its name is in SkipNames.
    /// </remarks>
    public static IEnumerable<WalkEntry> Walk(string root, WalkOptions options, CancellationToken cancellationToken)
    {
        var realRoot =
            SafePath.ResolveExisting(options.ContainRoot)
            ?? throw new CliException(
                ExitCodeConstants.Failure,
                $"Cannot walk: containment root '{options.ContainRoot}' does not exist or cannot be resolved.",
                title: "Unsafe tree walk");

        var realStart =
            SafePath.ResolveExisting(root)
            ?? throw new CliException(
                ExitCodeConstants.Failure,
                $"Cannot walk '{root}': it does not exist or cannot be resolved.",
                title: "Unsafe tree walk");

        if (!SafePath.IsContainedNormalized(realStart, realRoot))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Cannot walk '{root}': it is not contained in '{options.ContainRoot}'.",
                title: "Unsafe tree walk");
        }

        // Seed the visited-set with the root so a symlink pointing back at the root is caught.
        var visited = new HashSet<string>(SafePath.Comparer) { realRoot };
        return WalkCore(root, realStart, realRoot, options, visited, depth: 0, cancellationToken);
    }

    private static IEnumerable<WalkEntry> WalkCore(
        string logicalDir,
        string realDir,
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
                $"Maximum directory depth ({options.MaxDepth}) exceeded at '{logicalDir}'. A cyclic symlink may be present.",
                title: "Unsafe tree walk");
        }

        yield return new WalkEntry(logicalDir, realDir, WalkEntryKind.Directory);

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(realDir).EnumerateFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isReparse = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            var nameSkipped = options.SkipNames is { } skip && skip.Contains(entry.Name);
            var childLogical = Path.Combine(logicalDir, entry.Name);

            if (isReparse)
            {
                // Reparse decision runs BEFORE the skip-name check so Refuse refuses
                // every in-tree link regardless of name.
                foreach (
                    var yielded in HandleReparse(
                        entry,
                        childLogical,
                        realRoot,
                        options,
                        visited,
                        depth,
                        cancellationToken))
                {
                    yield return yielded;
                }

                continue;
            }

            if (nameSkipped)
            {
                continue;
            }

            // GetFullPath is the correct "real" value here: the parent chain (realDir) is
            // already resolved and this entry is non-reparse, so no symlink remains to follow.
            var childReal = Path.GetFullPath(entry.FullName);

            // Defense in depth: a non-reparse child of a contained, resolved dir is always
            // contained, but re-check turns any future regression into a refusal not an escape.
            if (!SafePath.IsContainedNormalized(childReal, realRoot) || !visited.Add(childReal))
            {
                continue;
            }

            if (entry is DirectoryInfo)
            {
                foreach (
                    var yielded in WalkCore(
                        childLogical,
                        childReal,
                        realRoot,
                        options,
                        visited,
                        depth + 1,
                        cancellationToken))
                {
                    yield return yielded;
                }
            }
            else
            {
                yield return new WalkEntry(childLogical, childReal, WalkEntryKind.File);
            }
        }
    }

    private static IEnumerable<WalkEntry> HandleReparse(
        FileSystemInfo entry,
        string childLogical,
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
                    $"Refusing to follow symlink '{entry.FullName}' during a no-follow tree walk.",
                    title: "Unsafe tree walk");

            case OnSymlink.FollowIfContained:
                var resolvedReal = SafePath.ResolveExisting(entry.FullName);
                if (resolvedReal is null || !SafePath.IsContainedNormalized(resolvedReal, realRoot))
                {
                    // Out-of-root or broken link: drop it (copy semantics). A symlink to
                    // ~/.ssh resolves out of root and is dropped, never followed and
                    // read/hashed.
                    yield break;
                }

                if (!visited.Add(resolvedReal))
                {
                    yield break; // already processed via another path
                }

                if (Directory.Exists(resolvedReal))
                {
                    foreach (
                        var yielded in WalkCore(
                            childLogical,
                            resolvedReal,
                            realRoot,
                            options,
                            visited,
                            depth + 1,
                            cancellationToken))
                    {
                        yield return yielded;
                    }
                }
                else
                {
                    yield return new WalkEntry(childLogical, resolvedReal, WalkEntryKind.File);
                }

                yield break;

            default:
                yield break;
        }
    }
}
