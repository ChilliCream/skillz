namespace Skillz.Plugins;

internal interface IPluginGrouping
{
    Task<Dictionary<string, string>> GetPluginGroupingsAsync(string basePath, CancellationToken cancellationToken);
}

internal sealed class PluginGrouping : IPluginGrouping
{
    public async Task<Dictionary<string, string>> GetPluginGroupingsAsync(
        string basePath,
        CancellationToken cancellationToken)
    {
        var groupings = new Dictionary<string, string>(StringComparer.Ordinal);

        await PluginManifest.TryReadMarketplaceAsync(
            basePath,
            (pluginBase, skills, name) =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }

                if (!PathContainment.IsContainedInRealPath(pluginBase, basePath))
                {
                    return;
                }

                if (skills is { Count: > 0 })
                {
                    foreach (var skillPath in skills)
                    {
                        AddGrouping(groupings, basePath, pluginBase, skillPath, name);
                    }
                }
            },
            cancellationToken);

        await PluginManifest.TryReadPluginJsonAsync(
            basePath,
            (skills, name) =>
            {
                if (string.IsNullOrEmpty(name) || skills is not { Count: > 0 })
                {
                    return;
                }

                foreach (var skillPath in skills)
                {
                    AddGrouping(groupings, basePath, basePath, skillPath, name);
                }
            },
            cancellationToken);

        return groupings;
    }

    private static void AddGrouping(
        Dictionary<string, string> groupings,
        string basePath,
        string skillBase,
        string skillPath,
        string name)
    {
        if (!PathContainment.IsValidRelativePath(skillPath))
        {
            return;
        }

        var skillDir = Path.Combine(skillBase, skillPath);
        // Normalize: if path ends with SKILL.md, use the parent directory
        var fullPath = Path.GetFullPath(skillDir);
        if (fullPath.EndsWithOrdinalIgnoreCase("/SKILL.md") || fullPath.EndsWithOrdinalIgnoreCase("\\SKILL.md"))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (PathContainment.IsContainedInRealPath(skillDir, basePath))
        {
            groupings[fullPath] = name;
        }
    }
}
