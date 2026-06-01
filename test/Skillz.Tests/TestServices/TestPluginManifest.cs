using Skillz.Plugins;

namespace Skillz.Tests.TestServices;

internal sealed class TestPluginManifest : IPluginManifest
{
    public Func<string, IReadOnlyList<string>>? OnGetPluginSkillPaths { get; set; }

    public Task<IReadOnlyList<string>> GetPluginSkillPathsAsync(
        string basePath,
        CancellationToken cancellationToken = default)
    {
        var result = OnGetPluginSkillPaths is not null ? OnGetPluginSkillPaths(basePath) : Array.Empty<string>();
        return Task.FromResult(result);
    }
}
