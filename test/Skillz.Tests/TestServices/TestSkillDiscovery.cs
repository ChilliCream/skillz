using Skillz.Skills;

namespace Skillz.Tests.TestServices;

internal sealed class TestSkillDiscovery : ISkillDiscovery
{
    public Func<string, string?, SkillDiscoveryOptions?, IReadOnlyList<Skill>>? OnDiscover { get; set; }

    public Task<IReadOnlyList<Skill>> DiscoverAsync(
        string basePath,
        string? subpath = null,
        SkillDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = OnDiscover is not null ? OnDiscover(basePath, subpath, options) : Array.Empty<Skill>();
        return Task.FromResult(result);
    }
}
