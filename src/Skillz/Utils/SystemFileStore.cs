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

    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        => File.ReadAllTextAsync(path, cancellationToken);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(path, content, cancellationToken);

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        => File.WriteAllBytesAsync(path, bytes, cancellationToken);
}
