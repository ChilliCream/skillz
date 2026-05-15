namespace Skillz.Skills;

internal interface ISkillDiscovery
{
    Task<IReadOnlyList<Skill>> DiscoverAsync(
        string basePath,
        string? subpath = null,
        SkillDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);
}

internal sealed record SkillDiscoveryOptions(
    bool IncludeInternal = false,
    bool FullDepth = false);
