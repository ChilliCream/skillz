namespace Skillz.Plugins;

internal interface IPluginGrouping
{
    Task<Dictionary<string, string>> GetPluginGroupingsAsync(string basePath);
}

internal sealed class PluginGrouping : IPluginGrouping
{
    public async Task<Dictionary<string, string>> GetPluginGroupingsAsync(string basePath)
    {
        var groupings = new Dictionary<string, string>();

        await PluginManifest.TryReadMarketplaceAsync(basePath, (pluginBase, skills, name) =>
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!PathContainment.IsContainedIn(pluginBase, basePath))
            {
                return;
            }

            if (skills is { Count: > 0 })
            {
                foreach (var skillPath in skills)
                {
                    if (!PathContainment.IsValidRelativePath(skillPath))
                    {
                        continue;
                    }

                    var skillDir = Path.Combine(pluginBase, skillPath);
                    if (PathContainment.IsContainedIn(skillDir, basePath))
                    {
                        groupings[Path.GetFullPath(skillDir)] = name;
                    }
                }
            }
        });

        await PluginManifest.TryReadPluginJsonAsync(basePath, (skills, name) =>
        {
            if (string.IsNullOrEmpty(name) || skills is not { Count: > 0 })
            {
                return;
            }

            foreach (var skillPath in skills)
            {
                if (!PathContainment.IsValidRelativePath(skillPath))
                {
                    continue;
                }

                var skillDir = Path.Combine(basePath, skillPath);
                if (PathContainment.IsContainedIn(skillDir, basePath))
                {
                    groupings[Path.GetFullPath(skillDir)] = name;
                }
            }
        });

        return groupings;
    }
}
