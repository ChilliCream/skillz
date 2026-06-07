using Skillz.Paths;
using Skillz.Utils;

namespace Skillz.Plugins;

internal sealed class PluginGrouping(IFileStore fileStore)
{
    public async Task<Dictionary<string, string>> GetPluginGroupingsAsync(
        string basePath,
        CancellationToken cancellationToken)
    {
        var groupings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var plugin in await PluginManifest.ReadPluginsAsync(fileStore, basePath, cancellationToken))
        {
            if (string.IsNullOrEmpty(plugin.Name)
                || plugin.Skills is not { Count: > 0 }
                || !SafePath.Contains(basePath, plugin.PluginBase, LeafPolicy.Preserve))
            {
                continue;
            }

            foreach (var skillPath in plugin.Skills)
            {
                AddGrouping(groupings, basePath, plugin.PluginBase, skillPath, plugin.Name);
            }
        }

        return groupings;
    }

    private static void AddGrouping(
        Dictionary<string, string> groupings,
        string basePath,
        string skillBase,
        string skillPath,
        string name)
    {
        if (!SafePath.IsValidManifestRelativePath(skillPath))
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

        if (SafePath.Contains(basePath, skillDir, LeafPolicy.Preserve))
        {
            groupings[fullPath] = name;
        }
    }
}
