namespace Skillz.Paths;

/// <summary>
/// Whether the final path component (the leaf) is followed through a symlink when
/// resolving for a containment check.
/// </summary>
internal enum LeafPolicy
{
    /// <summary>
    /// The leaf is NOT followed even if it is itself a symlink. The location the
    /// leaf sits in is what is contained, not where a leaf link points. A
    /// skillz-managed agent-&gt;canonical leaf symlink must not read as an escape.
    /// </summary>
    Preserve,

    /// <summary>
    /// The leaf IS followed through any symlink. Enumeration descends into the leaf,
    /// so "is it inside" must judge where the leaf really lands.
    /// </summary>
    Follow
}
