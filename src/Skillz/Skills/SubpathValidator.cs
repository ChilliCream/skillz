using Skillz.Plugins;

namespace Skillz.Skills;

/// <summary>
/// Guards caller-supplied subpaths against directory traversal so a scan or install can
/// never escape the base repository directory.
/// </summary>
internal static class SubpathValidator
{
    /// <summary>
    /// Validates that <paramref name="subpath"/> is a relative path free of <c>..</c> traversal
    /// segments and control characters, returning it unchanged when safe.
    /// </summary>
    /// <param name="subpath">The relative subpath to check (either slash style is accepted).</param>
    /// <returns>The original <paramref name="subpath"/> when it is safe.</returns>
    /// <exception cref="CliException">
    /// Thrown when <paramref name="subpath"/> contains a <c>..</c> segment, is absolute, or contains
    /// a control character.
    /// </exception>
    public static string ValidateSubpath(string subpath)
    {
        if (subpath.ContainsControlCharacter())
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                "Unsafe subpath: contains a disallowed control character.");
        }

        var normalized = subpath.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"""Unsafe subpath: "{subpath}" must be relative. Subpaths must not be absolute.""");
        }

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
    /// Checks that <paramref name="subpath"/>, resolved under <paramref name="basePath"/>, stays
    /// inside <paramref name="basePath"/> (or is <paramref name="basePath"/> itself) — so a
    /// source's subpath cannot make discovery read files outside the directory it came from.
    /// </summary>
    /// <remarks>
    /// Symlinks are followed on the real filesystem before the containment check, so a subpath
    /// that points outside <paramref name="basePath"/> through a symlink is rejected even though
    /// it reads as a clean relative path. A target that does not exist yet is resolved through its
    /// nearest existing parent, so the check works before the directory is created. Symlink
    /// resolution is delegated to <see cref="RealPath"/>.
    /// </remarks>
    /// <param name="basePath">The directory the subpath must stay within.</param>
    /// <param name="subpath">The relative subpath to resolve under <paramref name="basePath"/>.</param>
    /// <returns><see langword="true"/> when the resolved path is inside <paramref name="basePath"/>.</returns>
    public static bool IsSubpathSafe(string basePath, string subpath)
    {
        // Reject any ".." segment up front (it is never valid in a skill subpath). We must not let
        // it reach the resolver: ".." is collapsed as text before symlinks are followed, so
        // "link/../x" (link -> outside base) could look contained yet really escape.
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
