using Skillz.Plugins;

namespace Skillz.Tests.TestServices;

internal sealed class TestPluginGrouping : IPluginGrouping
{
    public Func<string, Dictionary<string, string>>? OnGetPluginGroupings { get; set; }

    public Task<Dictionary<string, string>> GetPluginGroupingsAsync(string basePath)
    {
        var result = OnGetPluginGroupings is not null ? OnGetPluginGroupings(basePath) : new Dictionary<string, string>(StringComparer.Ordinal);
        return Task.FromResult(result);
    }
}
