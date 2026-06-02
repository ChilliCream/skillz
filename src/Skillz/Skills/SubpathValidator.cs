namespace Skillz.Skills;

/// <summary>
/// Guards caller-supplied subpaths against directory traversal so a scan or install can
/// never escape the base repository directory.
/// </summary>
internal static class SubpathValidator
{
    /// <summary>
    /// Validates that <paramref name="subpath"/> contains no <c>..</c> traversal segments,
    /// returning it unchanged when safe.
    /// </summary>
    /// <param name="subpath">The relative subpath to check (either slash style is accepted).</param>
    /// <returns>The original <paramref name="subpath"/> when it is free of <c>..</c> segments.</returns>
    /// <exception cref="CliException">Thrown when <paramref name="subpath"/> contains a <c>..</c> segment.</exception>
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

    /// <summary>
    /// Returns whether <paramref name="subpath"/> resolves to a location inside
    /// <paramref name="basePath"/> (or to <paramref name="basePath"/> itself).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SanitizeSubpath"/>, which only rejects literal <c>..</c> segments,
    /// this resolves the combined path and verifies containment, catching escapes via
    /// symlinks or absolute components.
    /// </remarks>
    /// <param name="basePath">The directory the subpath must remain within.</param>
    /// <param name="subpath">The relative subpath to resolve against <paramref name="basePath"/>.</param>
    /// <returns><see langword="true"/> when the resolved path is contained in <paramref name="basePath"/>.</returns>
    public static bool IsSubpathSafe(string basePath, string subpath)
    {
        var combined = Path.GetFullPath(Path.Combine(basePath, subpath));
        var normalizedBase = Path.GetFullPath(basePath);
        return combined.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || combined == normalizedBase;
    }
}
