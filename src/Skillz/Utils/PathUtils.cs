namespace Skillz.Utils;

/// <summary>
/// Small helpers for working with filesystem paths.
/// </summary>
internal static class PathUtils
{
    /// <summary>
    /// Shortens <paramref name="path"/> for display by replacing a leading home directory with
    /// <c>~</c> or a leading current directory with <c>.</c>. Home takes precedence over cwd; a
    /// path under neither is returned unchanged.
    /// </summary>
    public static string Shorten(string path, string? home, string? cwd)
    {
        if (!string.IsNullOrEmpty(home) && IsWithinBase(path, home))
        {
            return "~" + path[home.Length..];
        }
        if (!string.IsNullOrEmpty(cwd) && IsWithinBase(path, cwd))
        {
            var relative = path[cwd.Length..];
            return "." + (relative.Length == 0 ? "" : relative);
        }
        return path;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> is exactly
    /// <paramref name="basePath"/> or a descendant of it, requiring a directory-separator
    /// boundary so a sibling whose name merely starts with <paramref name="basePath"/>
    /// (e.g. <c>/home/bobby</c> against base <c>/home/bob</c>) is not treated as contained.
    /// </summary>
    private static bool IsWithinBase(string path, string basePath)
    {
        if (!path.StartsWithOrdinal(basePath))
        {
            return false;
        }

        return path.Length == basePath.Length
            || path[basePath.Length] == Path.DirectorySeparatorChar;
    }
}
