namespace Skillz.Paths;

/// <summary>
/// What the tree walk does when it meets a reparse point (symlink / junction / mount).
/// Keyed on <see cref="FileAttributes.ReparsePoint"/> so junctions and mounts behave
/// identically to Unix symlinks.
/// </summary>
internal enum OnSymlink
{
    /// <summary>
    /// Throw - any reparse point in the tree aborts the walk.
    /// </summary>
    Refuse,

    /// <summary>
    /// Skip the reparse entry silently and continue (hash policy: an out-of-tree link contributes nothing).
    /// </summary>
    Skip,

    /// <summary>
    /// Follow the link only if its resolved target is still contained in the root, then descend; skip if it escapes (copy policy).
    /// </summary>
    FollowIfContained
}
