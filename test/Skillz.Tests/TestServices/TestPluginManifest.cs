using System.Collections.Immutable;
using Skillz.Plugins;

namespace Skillz.Tests.TestServices;

internal sealed class TestPluginManifest : IPluginManifest
{
    public Func<string, IReadOnlyList<string>>? OnGetPluginSkillPaths { get; set; }

    public Task<ImmutableArray<string>> GetPluginSkillPathsAsync(
        string basePath,
        CancellationToken cancellationToken)
    {
        var result = OnGetPluginSkillPaths is not null ? OnGetPluginSkillPaths(basePath) : [];
        return Task.FromResult<ImmutableArray<string>>([.. result]);
    }
}
