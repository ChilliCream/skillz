using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Tests.TestServices;

internal sealed class TestSkillDiscovery : ISkillDiscovery
{
    public Func<string, string?, SkillDiscoveryOptions?, IReadOnlyList<Skill>>? OnDiscover { get; set; }

    public Task<ImmutableArray<Skill>> DiscoverAsync(
        string basePath,
        string? subpath,
        SkillDiscoveryOptions? options,
        CancellationToken cancellationToken)
    {
        var result = OnDiscover is not null ? OnDiscover(basePath, subpath, options) : [];
        return Task.FromResult<ImmutableArray<Skill>>([.. result]);
    }
}
