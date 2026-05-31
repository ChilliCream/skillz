using System.Text.Json;

namespace Skillz.Plugins;

internal interface IPluginManifest
{
    Task<IReadOnlyList<string>> GetPluginSkillPathsAsync(string basePath);
}

internal sealed class PluginManifest : IPluginManifest
{
    public async Task<IReadOnlyList<string>> GetPluginSkillPathsAsync(string basePath)
    {
        var searchDirs = new List<string>();

        await TryReadMarketplaceAsync(
            basePath,
            (pluginBase, skills, _) => AddPluginSkillPaths(basePath, pluginBase, skills, searchDirs));

        await TryReadPluginJsonAsync(
            basePath,
            (skills, _) => AddPluginSkillPaths(basePath, basePath, skills, searchDirs));

        return searchDirs;
    }

    private static void AddPluginSkillPaths(
        string basePath,
        string pluginBase,
        List<string>? skills,
        List<string> searchDirs)
    {
        if (!PathContainment.IsContainedInRealPath(pluginBase, basePath))
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

                var skillDir = Path.GetDirectoryName(Path.Combine(pluginBase, skillPath));
                if (skillDir is null)
                {
                    continue;
                }

                if (PathContainment.IsContainedInRealPath(skillDir, basePath))
                {
                    searchDirs.Add(skillDir);
                }
            }
        }

        searchDirs.Add(Path.Combine(pluginBase, "skills"));
    }

    internal static async Task TryReadMarketplaceAsync(string basePath, Action<string, List<string>?, string?> onPlugin)
    {
        var marketplacePath = Path.Combine(basePath, ".claude-plugin", "marketplace.json");
        MarketplaceManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(marketplacePath);
            manifest = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.MarketplaceManifest);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (JsonException)
        {
            return;
        }

        if (manifest is null)
        {
            return;
        }

        var pluginRoot = manifest.Metadata?.PluginRoot;
        if (pluginRoot is not null && !PathContainment.IsValidRelativePath(pluginRoot))
        {
            return;
        }

        if (manifest.Plugins is null)
        {
            return;
        }

        foreach (var plugin in manifest.Plugins)
        {
            string? sourceString = null;
            if (plugin.Source is { } sourceElement)
            {
                if (sourceElement.ValueKind == JsonValueKind.String)
                {
                    sourceString = sourceElement.GetString();
                }
                else if (sourceElement.ValueKind == JsonValueKind.Null
                    || sourceElement.ValueKind == JsonValueKind.Undefined)
                {
                    sourceString = null;
                }
                else
                {
                    continue;
                }
            }

            if (sourceString is not null && !PathContainment.IsValidRelativePath(sourceString))
            {
                continue;
            }

            var pluginBase = Path.Combine(basePath, pluginRoot ?? string.Empty, sourceString ?? string.Empty);
            onPlugin(pluginBase, plugin.Skills, plugin.Name);
        }
    }

    internal static async Task TryReadPluginJsonAsync(string basePath, Action<List<string>?, string?> onPlugin)
    {
        var pluginPath = Path.Combine(basePath, ".claude-plugin", "plugin.json");
        SinglePluginManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(pluginPath);
            manifest = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSourceGenerationContext.Default.SinglePluginManifest);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (JsonException)
        {
            return;
        }

        if (manifest is null)
        {
            return;
        }

        onPlugin(manifest.Skills, manifest.Name);
    }
}
