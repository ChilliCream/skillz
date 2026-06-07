namespace Skillz.Paths;

/// <summary>
/// One node yielded by <see cref="SafeTreeWalker.Walk"/>. <see cref="RealPath"/> is the
/// resolved, contained real path; <see cref="LogicalPath"/> is the path as walked
/// (for relative-path math, e.g. hashing).
/// </summary>
/// <param name="LogicalPath">The path as walked, used for relative-path math.</param>
/// <param name="RealPath">The resolved, contained real path.</param>
/// <param name="Kind">Whether the entry is a file or a directory.</param>
internal readonly record struct WalkEntry(string LogicalPath, string RealPath, WalkEntryKind Kind);
