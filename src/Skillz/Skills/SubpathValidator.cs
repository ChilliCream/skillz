using Skillz.Plugins;

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
    public static string ValidateSubpath(string subpath)
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
    /// Unlike <see cref="ValidateSubpath"/>, which only rejects literal <c>..</c> segments,
    /// this follows symlinks on the combined target before checking containment, so a subpath
    /// that is (or passes through) a symlink pointing outside <paramref name="basePath"/> is
    /// rejected even though it is lexically clean. Resolution is delegated to
    /// <see cref="RealPath"/> rather than duplicated here. A subpath that does not exist on
    /// disk yet is resolved through its nearest existing parent, so the check still works
    /// before the target is created.
    /// </remarks>
    /// <param name="basePath">The directory the subpath must remain within.</param>
    /// <param name="subpath">The relative subpath to resolve against <paramref name="basePath"/>.</param>
    /// <returns><see langword="true"/> when the resolved path is contained in <paramref name="basePath"/>.</returns>
    public static bool IsSubpathSafe(string basePath, string subpath)
    {
        // Fast lexical pre-check: a literal ".." segment can never be safe, and rejecting it
        // here avoids touching the filesystem for the common attack shape.
        var normalized = subpath.Replace('\\', '/');
        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        // Authoritative check: resolve the combined target's symlinks (including the leaf,
        // which discovery would otherwise enumerate into) and the base, then compare real
        // paths so an escape via a symlink anywhere on the path is caught.
        var combined = Path.Combine(basePath, subpath);
        var realCombined = RealPath.ResolveWithNearestExistingParent(combined);
        var realBase = RealPath.ResolveWithNearestExistingParent(basePath);

        return realCombined is not null
            && realBase is not null
            && PathContainment.IsContainedIn(realCombined, realBase);
    }
}
