namespace Skillz.Plugins;

internal static class PathContainment
{
    public static bool IsContainedIn(string targetPath, string basePath)
    {
        var normalizedBase = Path.GetFullPath(basePath);
        var normalizedTarget = Path.GetFullPath(targetPath);

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return normalizedTarget.StartsWith(normalizedBase + Path.DirectorySeparatorChar, comparison)
            || string.Equals(normalizedTarget, normalizedBase, comparison);
    }

    public static bool IsValidRelativePath(string path)
    {
        return path.StartsWith("./", StringComparison.Ordinal);
    }
}
