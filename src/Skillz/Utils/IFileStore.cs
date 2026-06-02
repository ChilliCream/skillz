namespace Skillz.Utils;

/// <summary>
/// Abstracts the filesystem content and structure operations that command and discovery
/// logic depend on — reading and writing files, and creating, enumerating, and deleting
/// directories — so that logic can be substituted with an in-memory store in tests instead
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
    /// Returns the immediate child directories of the given path.
    /// </summary>
    IEnumerable<string> EnumerateDirectories(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);
}
