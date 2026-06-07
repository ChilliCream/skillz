namespace Skillz.Paths;

/// <summary>
/// Kind of entry yielded by <see cref="SafeTreeWalker.Walk"/>.
/// </summary>
internal enum WalkEntryKind
{
    /// <summary>
    /// A regular file.
    /// </summary>
    File,

    /// <summary>
    /// A directory.
    /// </summary>
    Directory
}
