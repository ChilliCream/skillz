using System.Collections.Immutable;

namespace Skillz.Skills;

internal interface ISkillDiscovery
{
    Task<ImmutableArray<Skill>> DiscoverAsync(
        string basePath,
        string? subpath,
        SkillDiscoveryOptions? options,
        CancellationToken cancellationToken);
}

internal sealed record SkillDiscoveryOptions(bool IncludeInternal = false, bool FullDepth = false);
