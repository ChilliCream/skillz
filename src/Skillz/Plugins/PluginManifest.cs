using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Skillz.Skills;
using Skillz.Utils;

namespace Skillz.Plugins;

/// <summary>
/// A plugin resolved from a manifest: its base directory, declared skill paths,
/// and name. Produced by <see cref="PluginManifest.ReadPluginsAsync"/> and shared
/// by both skill-path discovery and grouping.
/// </summary>
internal readonly record struct PluginEntry(string PluginBase, List<string>? Skills, string? Name);

internal sealed class PluginManifest(IFileStore fileStore)
{
    public async Task<ImmutableArray<string>> GetPluginSkillPathsAsync(
        string basePath,
        CancellationToken cancellationToken)
    {
        var searchDirs = new List<string>();

        foreach (var plugin in await ReadPluginsAsync(fileStore, basePath, cancellationToken))
        {
            AddPluginSkillPaths(basePath, plugin, searchDirs);
        }

        return [.. searchDirs];
    }

    private static void AddPluginSkillPaths(string basePath, PluginEntry plugin, List<string> searchDirs)
    {
        if (!PathContainment.IsContainedInRealPath(plugin.PluginBase, basePath))
        {
            return;
        }

        if (plugin.Skills is { Count: > 0 })
        {
            foreach (var skillPath in plugin.Skills)
            {
                if (!PathContainment.IsValidRelativePath(skillPath))
                {
                    continue;
                }

                var skillDir = Path.GetDirectoryName(Path.Combine(plugin.PluginBase, skillPath));
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

        searchDirs.Add(Path.Combine(plugin.PluginBase, "skills"));
    }

    /// <summary>
    /// Reads <c>marketplace.json</c> then <c>plugin.json</c> under
    /// <paramref name="basePath"/> and returns the plugins each declares.
    /// Missing or malformed manifests contribute no entries.
    /// </summary>
    internal static async Task<IReadOnlyList<PluginEntry>> ReadPluginsAsync(
        IFileStore fileStore,
        string basePath,
        CancellationToken cancellationToken)
    {
        var plugins = new List<PluginEntry>();

        await AddMarketplacePluginsAsync(fileStore, basePath, plugins, cancellationToken);
        await AddSinglePluginAsync(fileStore, basePath, plugins, cancellationToken);

        return plugins;
    }

    private static async Task AddMarketplacePluginsAsync(
        IFileStore fileStore,
        string basePath,
        List<PluginEntry> plugins,
        CancellationToken cancellationToken)
    {
        var manifest = await TryDeserializeAsync(
            fileStore,
            Path.Combine(basePath, ".claude-plugin", "marketplace.json"),
            JsonSourceGenerationContext.Default.MarketplaceManifest,
            cancellationToken);

        if (manifest?.Plugins is null)
        {
            return;
        }

        var pluginRoot = manifest.Metadata?.PluginRoot;
        if (pluginRoot is not null && !PathContainment.IsValidRelativePath(pluginRoot))
        {
            return;
        }

        foreach (var plugin in manifest.Plugins)
        {
            if (!TryResolveSource(plugin.Source, out var sourceString))
            {
                continue;
            }

            var pluginBase = Path.Combine(basePath, pluginRoot ?? string.Empty, sourceString ?? string.Empty);
            var name = plugin.Name is null ? null : TerminalSanitizer.SanitizeMetadata(plugin.Name);
            plugins.Add(new PluginEntry(pluginBase, plugin.Skills, name));
        }
    }

    private static async Task AddSinglePluginAsync(
        IFileStore fileStore,
        string basePath,
        List<PluginEntry> plugins,
        CancellationToken cancellationToken)
    {
        var manifest = await TryDeserializeAsync(
            fileStore,
            Path.Combine(basePath, ".claude-plugin", "plugin.json"),
            JsonSourceGenerationContext.Default.SinglePluginManifest,
            cancellationToken);

        if (manifest is not null)
        {
            var name = manifest.Name is null ? null : TerminalSanitizer.SanitizeMetadata(manifest.Name);
            plugins.Add(new PluginEntry(basePath, manifest.Skills, name));
        }
    }

    /// <summary>
    /// Resolves a plugin's <c>source</c> to a relative path. A string source is a
    /// relative path; a null/undefined source means the plugin lives at the root.
    /// Returns <see langword="false"/> to skip the plugin when the source is an
    /// unsupported value (e.g. a remote <c>{ source, repo }</c> object) or not a
    /// valid relative path.
    /// </summary>
    private static bool TryResolveSource(JsonElement? source, out string? sourceString)
    {
        sourceString = null;

        if (source is { } sourceElement)
        {
            if (sourceElement.ValueKind == JsonValueKind.String)
            {
                sourceString = sourceElement.GetString();
            }
            else if (sourceElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                return false;
            }
        }

        return sourceString is null || PathContainment.IsValidRelativePath(sourceString);
    }

    private static async Task<T?> TryDeserializeAsync<T>(
        IFileStore fileStore,
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        string json;
        try
        {
            json = await fileStore.ReadAllTextAsync(path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
