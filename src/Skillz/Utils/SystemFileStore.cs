namespace Skillz.Utils;

internal sealed class SystemFileStore : IFileStore
{
    public bool PathExists(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeletePath(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex)
            when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            // Delete the symlink as a link — never recurse through it, so the
            // link target's contents are left untouched. This holds whether the
            // target is a directory, a file, or gone (a broken symlink).
            DeleteReparsePoint(path);
        }
        else if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }

    private static void DeleteReparsePoint(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(path);
        }
        catch (IOException)
        {
            Directory.Delete(path);
        }
    }

    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);

    public bool IsDirectoryEmpty(string path)
    {
        if (!Directory.Exists(path))
        {
            return true;
        }

        return !Directory.EnumerateFileSystemEntries(path).Any();
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        => File.ReadAllTextAsync(path, cancellationToken);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(path, content, cancellationToken);

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        => File.WriteAllBytesAsync(path, bytes, cancellationToken);
}
