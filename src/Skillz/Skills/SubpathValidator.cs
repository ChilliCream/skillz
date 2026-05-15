namespace Skillz.Skills;

internal static class SubpathValidator
{
    public static string SanitizeSubpath(string subpath)
    {
        var normalized = subpath.Replace('\\', '/');
        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                throw new CliException(
                    ExitCodeConstants.Failure,
                    $"""Unsafe subpath: "{subpath}" contains path traversal segments. Subpaths must not contain ".." components.""");
            }
        }

        return subpath;
    }

    public static bool IsSubpathSafe(string basePath, string subpath)
    {
        var combined = Path.GetFullPath(Path.Combine(basePath, subpath));
        var normalizedBase = Path.GetFullPath(basePath);
        return combined.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || combined == normalizedBase;
    }
}
