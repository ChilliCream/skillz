using Skillz;
using Skillz.Utils;

namespace Skillz.Tests.TestServices;

internal sealed class FakeFileStore : IFileStore
{
    public Dictionary<string, byte[]> Files { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Dirs { get; init; } = new(StringComparer.Ordinal);

    public bool PathExists(string path)
    {
        var normalized = Normalize(path);
        return Files.ContainsKey(normalized) || Dirs.Contains(normalized);
    }

    // The in-memory store does not model symlinks.
    public bool IsSymlink(string path) => false;

    public bool FileExists(string path) => Files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => Dirs.Contains(Normalize(path));

    public void CreateDirectory(string path) => AddDirectoryWithAncestors(path);

    public void DeleteDirectory(string path, bool recursive)
    {
        var normalized = Normalize(path);
        if (recursive)
        {
            var prefix = normalized + "/";
            Dirs.RemoveWhere(d => d == normalized || d.StartsWithOrdinal(prefix));
            foreach (var file in Files.Keys.ToList())
            {
                if (file == normalized || file.StartsWithOrdinal(prefix))
                {
                    Files.Remove(file);
                }
            }
        }
        else
        {
            Dirs.Remove(normalized);
        }
    }

    public void DeleteFile(string path) => Files.Remove(Normalize(path));

    public void DeletePath(string path)
    {
        if (DirectoryExists(path))
        {
            DeleteDirectory(path, recursive: true);
        }
        else if (FileExists(path))
        {
            DeleteFile(path);
        }
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var normalized = Normalize(path);
        if (!Dirs.Contains(normalized))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        var prefix = normalized + "/";
        return Dirs
            .Where(d => d.StartsWithOrdinal(prefix) && !d[prefix.Length..].Contains('/'))
            .ToList();
    }

    public bool IsDirectoryEmpty(string path)
    {
        var normalized = Normalize(path);
        if (!Dirs.Contains(normalized))
        {
            return true;
        }

        var prefix = normalized + "/";
        var hasChildDir = Dirs.Any(d => d.StartsWithOrdinal(prefix));
        var hasChildFile = Files.Keys.Any(f => f.StartsWithOrdinal(prefix));
        return !hasChildDir && !hasChildFile;
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        var normalized = Normalize(path);
        if (!Files.TryGetValue(normalized, out var bytes))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        return Task.FromResult(System.Text.Encoding.UTF8.GetString(bytes));
    }

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        WriteBytes(path, System.Text.Encoding.UTF8.GetBytes(content));
        return Task.CompletedTask;
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        WriteBytes(path, bytes);
        return Task.CompletedTask;
    }

    private void WriteBytes(string path, byte[] bytes)
    {
        var normalized = Normalize(path);
        Files[normalized] = bytes;

        var parent = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(parent))
        {
            AddDirectoryWithAncestors(parent);
        }
    }

    private void AddDirectoryWithAncestors(string path)
    {
        var current = Normalize(path);
        while (!string.IsNullOrEmpty(current) && Dirs.Add(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent is null ? string.Empty : Normalize(parent);
        }
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }
}
