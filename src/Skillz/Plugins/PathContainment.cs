namespace Skillz.Plugins;

internal static class PathContainment
{
    internal static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    internal static StringComparer Comparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool IsContainedIn(string targetPath, string basePath)
    {
        var normalizedBase = Path.GetFullPath(basePath);
        var normalizedTarget = Path.GetFullPath(targetPath);

        return IsContainedNormalized(normalizedTarget, normalizedBase);
    }

    public static bool IsContainedInRealPath(string targetPath, string basePath)
    {
        // Resolve the base fully (it is an existing directory we compare
        // against) but only resolve the target's parent chain — the target's
        // leaf is treated literally. A leaf that is itself a symlink (e.g. a
        // skillz-managed agent->canonical link, or a stale self-loop) must not
        // be read as "escaping"; we only defend against a symlinked *parent*
        // redirecting the destination outside the base.
        var normalizedBase = RealPath.ResolveWithNearestExistingParent(basePath);
        var normalizedTarget = RealPath.ResolveParentPreservingLeaf(targetPath);

        return normalizedBase is not null
            && normalizedTarget is not null
            && IsContainedNormalized(normalizedTarget, normalizedBase);
    }

    public static bool IsValidRelativePath(string path)
    {
        return path.StartsWithOrdinal("./");
    }

    private static bool IsContainedNormalized(string normalizedTarget, string normalizedBase)
    {
        var baseWithSeparator = Path.TrimEndingDirectorySeparator(normalizedBase) + Path.DirectorySeparatorChar;

        return normalizedTarget.StartsWith(baseWithSeparator, Comparison)
            || string.Equals(
                Path.TrimEndingDirectorySeparator(normalizedTarget),
                Path.TrimEndingDirectorySeparator(normalizedBase),
                Comparison);
    }
}
