using System.Collections.Immutable;

namespace Skillz.Skills;

/// <summary>
/// Locates skills on disk by scanning for <c>SKILL.md</c> files under a given path.
/// </summary>
internal interface ISkillDiscovery
{
    /// <summary>
    /// Discovers all skills reachable from <paramref name="basePath"/>, parsing and
    /// deduplicating each <c>SKILL.md</c> that is found.
    /// </summary>
    /// <param name="basePath">The root directory to search from.</param>
    /// <param name="subpath">
    /// An optional path relative to <paramref name="basePath"/> to scope the search to.
    /// Must not escape <paramref name="basePath"/> via <c>..</c> segments.
    /// </param>
    /// <param name="options">
    /// Discovery options controlling internal-skill visibility and search depth.
    /// Defaults are used when <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the scan.</param>
    /// <returns>The discovered skills, deduplicated by sanitized name.</returns>
    Task<ImmutableArray<Skill>> DiscoverAsync(
        string basePath,
        string? subpath,
        SkillDiscoveryOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options that control how <see cref="ISkillDiscovery"/> scans for skills.
/// </summary>
/// <param name="IncludeInternal">
/// When <see langword="true"/>, skills marked <c>internal: true</c> in their metadata
/// are included instead of being filtered out.
/// </param>
/// <param name="FullDepth">
/// When <see langword="true"/>, the full directory tree is scanned even after skills are
/// found in priority locations, instead of short-circuiting on the first matches.
/// </param>
internal sealed record SkillDiscoveryOptions(bool IncludeInternal = false, bool FullDepth = false)
{
    public static SkillDiscoveryOptions Default => new();
}
