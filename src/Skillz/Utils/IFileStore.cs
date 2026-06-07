using Microsoft.Win32.SafeHandles;
using Skillz.Paths;

namespace Skillz.Utils;

/// <summary>
/// Abstracts the filesystem content and structure operations that command and discovery
/// logic depend on - reading and writing files, and creating, enumerating, and deleting
/// directories - so that logic can be substituted with an in-memory store in tests instead
/// of touching the real filesystem.
/// </summary>
internal interface IFileStore
{
    /// <summary>
    /// Determines whether anything (file, directory, or symlink/reparse point) exists at
    /// the given path.
    /// </summary>
    bool PathExists(string path);

    /// <summary>
    /// Determines whether the path is itself a symlink (reparse point), without following it.
    /// Returns <see langword="false"/> for a regular file or directory, or when nothing exists.
    /// </summary>
    bool IsSymlink(string path);

    /// <summary>
    /// Determines whether a file exists at the given path.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Determines whether a directory exists at the given path.
    /// </summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Creates the directory at the given path, including any missing ancestors.
    /// </summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Deletes the directory at the given path. When <paramref name="recursive"/> is
    /// <see langword="true"/>, its contents are deleted as well.
    /// </summary>
    void DeleteDirectory(string path, bool recursive);

    /// <summary>
    /// Deletes the file at the given path.
    /// </summary>
    void DeleteFile(string path);

    /// <summary>
    /// Deletes whatever exists at the given path. A reparse point (symlink) is removed as a
    /// link without recursing through it, so the target it points at is never touched; a real
    /// directory is deleted recursively, and a file is deleted directly.
    /// </summary>
    void DeletePath(string path);

    /// <summary>
    /// Returns the immediate child directories of the given path.
    /// </summary>
    IEnumerable<string> EnumerateDirectories(string path);

    /// <summary>
    /// Determines whether the directory at the given path contains no entries (no files and
    /// no subdirectories). A path that does not exist is treated as empty.
    /// </summary>
    bool IsDirectoryEmpty(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the file at <paramref name="path"/> for reading WITHOUT following a symlinked
    /// leaf, after confirming it is contained in <paramref name="containRoot"/>. Throws when
    /// the final path component is a reparse point or the path escapes the root. Backed by
    /// <see cref="SafePath.OpenReadNoFollow"/>.
    /// </summary>
    SafeFileHandle OpenReadNoFollow(string path, string containRoot);

    /// <summary>
    /// Reads all text of <paramref name="path"/> WITHOUT following a symlinked leaf, after
    /// confirming it is contained in <paramref name="containRoot"/>. Throws when the final path
    /// component is a reparse point or the path escapes the root. Backed by
    /// <see cref="SafePath.ReadAllTextNoFollowAsync"/>.
    /// </summary>
    Task<string> ReadAllTextNoFollowAsync(string path, string containRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> WITHOUT following a symlinked
    /// leaf, after confirming it is contained in <paramref name="containRoot"/>. Throws when the
    /// final path component is a reparse point or the path escapes the root. Backed by
    /// <see cref="SafePath.WriteAllBytesNoFollowAsync"/>.
    /// </summary>
    Task WriteAllBytesNoFollowAsync(string path, byte[] bytes, string containRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates the tree under <paramref name="root"/> with one uniform symlink policy.
    /// Every yielded entry's real path is contained in <see cref="WalkOptions.ContainRoot"/>;
    /// recursion is depth-bounded and real paths are de-duplicated. Backed by
    /// <see cref="SafeTreeWalker.Walk"/>.
    /// </summary>
    IEnumerable<WalkEntry> Walk(string root, WalkOptions options, CancellationToken cancellationToken);
}
