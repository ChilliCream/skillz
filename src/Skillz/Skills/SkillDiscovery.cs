using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Skillz.Install;
using Skillz.Plugins;
using Skillz.Utils;

namespace Skillz.Skills;

/// <summary>
/// Default <see cref="ISkillDiscovery"/> implementation that scans the filesystem for
/// <c>SKILL.md</c>-defined skills.
/// </summary>
/// <remarks>
/// Searching proceeds from cheapest to most expensive: a root <c>SKILL.md</c> (the
/// "this directory is a skill" case) is checked first, then a curated set of
/// convention-based priority directories (<c>s_priorityRelativeDirs</c>) plus any
/// plugin-provided paths, and finally a bounded recursive crawl that only runs when
/// nothing was found yet or <see cref="SkillDiscoveryOptions.FullDepth"/> is requested.
/// Results are deduplicated by sanitized name, so a skill found in an earlier (higher
/// priority) location wins over a later duplicate.
/// </remarks>
internal sealed class SkillDiscovery(
    IPluginManifest pluginManifest,
    IPluginGrouping pluginGrouping,
    IFileStore fileStore,
    ISystemEnvironment system) : ISkillDiscovery
{
    /// <summary>
    /// Maximum directory depth walked by the recursive fallback crawl.
    /// </summary>
    private const int MaxDepth = 5;

    /// <summary>Directory names skipped during the recursive crawl to avoid noise and large trees.</summary>
    private static readonly HashSet<string> s_skipDirs = new(StringComparer.Ordinal)
    {
        "node_modules",
        ".git",
        "dist",
        "build",
        "__pycache__"
    };

    /// <summary>
    /// Convention-based locations searched (one level deep) before falling back to a full
    /// crawl, in priority order. The empty string represents the search path itself.
    /// </summary>
    private static readonly string[] s_priorityRelativeDirs =
    [
        "",
        "skills",
        "skills/.curated",
        "skills/.experimental",
        "skills/.system",
        ".agents/skills",
        ".claude/skills",
        ".cline/skills",
        ".codebuddy/skills",
        ".codex/skills",
        ".commandcode/skills",
        ".continue/skills",
        ".github/skills",
        ".goose/skills",
        ".iflow/skills",
        ".junie/skills",
        ".kilocode/skills",
        ".kiro/skills",
        ".mux/skills",
        ".neovate/skills",
        ".opencode/skills",
        ".openhands/skills",
        ".pi/skills",
        ".qoder/skills",
        ".roo/skills",
        ".trae/skills",
        ".windsurf/skills",
        ".zencoder/skills"
    ];

    /// <inheritdoc />
    public async Task<ImmutableArray<Skill>> DiscoverAsync(
        string basePath,
        string? subpath,
        SkillDiscoveryOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= SkillDiscoveryOptions.Default;

        // Reject a subpath that would resolve outside the base repository directory.
        if (subpath is not null && !SubpathValidator.IsSubpathSafe(basePath, subpath))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"""Invalid subpath: "{subpath}" resolves outside the repository directory. Subpath must not contain ".." segments that escape the base path.""");
        }
        var searchPath = subpath is null ? basePath : Path.Combine(basePath, subpath);

        // Plugin metadata, used to tag each discovered skill with its plugin grouping.
        var groupings = await pluginGrouping.GetPluginGroupingsAsync(searchPath, cancellationToken);

        var skills = new List<Skill>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        async Task AddSkill(string dir)
        {
            if (!fileStore.FileExists(Path.Combine(dir, KnownConfigNames.SkillFileName)))
            {
                return;
            }

            var skill = await ParseSkillMdAsync(Path.Combine(dir, KnownConfigNames.SkillFileName), cancellationToken);

            if (skill is null)
            {
                return;
            }

            if (skill.IsInternal
                && !options.IncludeInternal
                && system.GetEnvironmentVariable("INSTALL_INTERNAL_SKILLS") is not "1" and not "true")
            {
                return;
            }

            if (!seenNames.Add(SkillNameSanitizer.SanitizeName(skill.Name)))
            {
                return;
            }

            skills.Add(
                groupings.TryGetValue(Path.GetFullPath(skill.Path), out var pluginName)
                    ? skill with { PluginName = pluginName }
                    : skill);
        }

        // Walk the source once and keep the first skill found for each sanitized name, tagging it
        // with its owning plugin when its resolved path matches a known grouping.
        await foreach (var dir in EnumerateSkillsAsync(searchPath, options, cancellationToken))
        {
            await AddSkill(dir);
        }

        // If we found nothing and didn't do a full scan yet, fall back to a crawl of the entire tree up to a max depth.
        if (skills.Count == 0 || options.FullDepth)
        {
            foreach (var dir in EnumerateCrawlSkillDirs(searchPath, depth: 0, cancellationToken))
            {
                await AddSkill(dir);
            }
        }

        return [.. skills];
    }

    private async IAsyncEnumerable<string> EnumerateSkillsAsync(
        string searchPath,
        SkillDiscoveryOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The source directory is itself a skill; a shallow scan needs nothing more.
        yield return searchPath;

        if (!options.FullDepth)
        {
            yield break;
        }

        // Fast path: convention-based priority roots plus plugin paths, scanned one level deep.
        var priorityRoots = new List<string>(s_priorityRelativeDirs.Length + 4);
        foreach (var relative in s_priorityRelativeDirs)
        {
            priorityRoots.Add(relative.Length == 0 ? searchPath : Path.Combine(searchPath, relative));
        }
        priorityRoots.AddRange(await pluginManifest.GetPluginSkillPathsAsync(searchPath, cancellationToken));

        foreach (var priorityRoot in priorityRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> entries;
            try
            {
                entries = fileStore.EnumerateDirectories(priorityRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // Missing or inaccessible directory - nothing to scan here.
                continue;
            }

            foreach (var dir in entries)
            {
                yield return dir;
            }
        }
    }

    private IEnumerable<string> EnumerateCrawlSkillDirs(
        string dir,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
        {
            yield break;
        }

        cancellationToken.ThrowIfCancellationRequested();

        yield return dir;

        string[] subDirs;
        try
        {
            subDirs = fileStore.EnumerateDirectories(dir).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            subDirs = [];
        }

        foreach (var subDir in subDirs)
        {
            var name = Path.GetFileName(subDir);
            if (s_skipDirs.Contains(name))
            {
                continue;
            }

            foreach (var skillDir in EnumerateCrawlSkillDirs(subDir, depth + 1, cancellationToken))
            {
                yield return skillDir;
            }
        }
    }

    /// <summary>
    /// Reads and parses a <c>SKILL.md</c> file into a <see cref="Skill"/>. Parsing is policy-free;
    /// the caller decides whether to keep the result (e.g. internal-skill filtering).
    /// </summary>
    /// <returns>
    /// The parsed skill, or <see langword="null"/> when the file is unreadable, has invalid
    /// frontmatter, or is missing a non-empty <c>name</c>/<c>description</c>. Name and description
    /// are run through <see cref="TerminalSanitizer"/> to neutralize untrusted terminal escapes.
    /// </returns>
    private async Task<Skill?> ParseSkillMdAsync(string skillMdPath, CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await fileStore.ReadAllTextAsync(skillMdPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        FrontmatterResult parsed;
        try
        {
            parsed = FrontmatterParser.Parse(content);
        }
        catch
        {
            return null;
        }

        if (!parsed.Data.TryGetValue("name", out var nameObj)
            || !parsed.Data.TryGetValue("description", out var descObj))
        {
            return null;
        }

        if (nameObj is not string name || descObj is not string description)
        {
            return null;
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(description))
        {
            return null;
        }

        Dictionary<string, object>? metadata = null;
        if (parsed.Data.TryGetValue("metadata", out var metadataObj)
            && metadataObj is Dictionary<object, object> rawMetadata)
        {
            metadata = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kvp in rawMetadata)
            {
                if (kvp.Key is string key && kvp.Value is not null)
                {
                    metadata[key] = kvp.Value;
                }
            }
        }

        var skillDir = Path.GetDirectoryName(skillMdPath) ?? skillMdPath;

        return new Skill(
            Name: TerminalSanitizer.SanitizeMetadata(name),
            Description: TerminalSanitizer.SanitizeMetadata(description),
            Path: skillDir,
            RawContent: content,
            Metadata: metadata);
    }
}

file static class Extensions
{
    extension(Skill skill)
    {
        /// <summary>Returns whether <paramref name="skill"/> is marked <c>internal: true</c> in its metadata.</summary>
        public bool IsInternal
            => skill.Metadata is { } metadata
            && metadata.TryGetValue("internal", out var flag)
            && flag is string value
            && value.EqualsOrdinalIgnoreCase("true");
    }
}
