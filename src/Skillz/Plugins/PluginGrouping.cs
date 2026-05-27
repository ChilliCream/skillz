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
                    // Normalize: if path ends with SKILL.md, use the parent directory
                    var fullPath = Path.GetFullPath(skillDir);
                    if (fullPath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
                        || fullPath.EndsWith("\\SKILL.md", StringComparison.OrdinalIgnoreCase))
                    {
                        fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
                    }

                    if (PathContainment.IsContainedIn(skillDir, basePath))
                    {
                        groupings[fullPath] = name;
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
                // Normalize: if path ends with SKILL.md, use the parent directory
                var fullPath = Path.GetFullPath(skillDir);
                if (fullPath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
                    || fullPath.EndsWith("\\SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
                }

                if (PathContainment.IsContainedIn(skillDir, basePath))
                {
                    groupings[fullPath] = name;
                }
            }
        });

        return groupings;
    }
}
