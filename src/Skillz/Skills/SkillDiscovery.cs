using Skillz.Plugins;

namespace Skillz.Skills;

internal sealed class SkillDiscovery : ISkillDiscovery
{
    private const int MaxDepth = 5;

    private static readonly HashSet<string> SkipDirs = new(StringComparer.Ordinal)
    {
        "node_modules",
        ".git",
        "dist",
        "build",
        "__pycache__"
    };

    private static readonly string[] PriorityRelativeDirs =
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
        ".zencoder/skills",
    ];

    private readonly IPluginManifest _pluginManifest;
    private readonly IPluginGrouping _pluginGrouping;

    public SkillDiscovery(IPluginManifest pluginManifest, IPluginGrouping pluginGrouping)
    {
        _pluginManifest = pluginManifest;
        _pluginGrouping = pluginGrouping;
    }

    public async Task<IReadOnlyList<Skill>> DiscoverAsync(
        string basePath,
        string? subpath = null,
        SkillDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SkillDiscoveryOptions();

        if (subpath is not null && !SubpathValidator.IsSubpathSafe(basePath, subpath))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"""Invalid subpath: "{subpath}" resolves outside the repository directory. Subpath must not contain ".." segments that escape the base path.""");
        }

        var searchPath = subpath is null
            ? basePath
            : Path.Combine(basePath, subpath);

        var groupings = await _pluginGrouping.GetPluginGroupingsAsync(searchPath).ConfigureAwait(false);

        var skills = new List<Skill>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        if (HasSkillMd(searchPath))
        {
            var rootSkill = await ParseSkillMdAsync(
                Path.Combine(searchPath, KnownConfigNames.SkillFileName),
                options,
                cancellationToken).ConfigureAwait(false);

            if (rootSkill is not null)
            {
                rootSkill = Enhance(rootSkill, groupings);
                skills.Add(rootSkill);
                seenNames.Add(NameSanitizer.SanitizeName(rootSkill.Name));

                if (!options.FullDepth)
                {
                    return skills;
                }
            }
        }

        var prioritySearchDirs = new List<string>(PriorityRelativeDirs.Length + 4);
        foreach (var relative in PriorityRelativeDirs)
        {
            prioritySearchDirs.Add(relative.Length == 0 ? searchPath : Path.Combine(searchPath, relative));
        }

        var pluginPaths = await _pluginManifest.GetPluginSkillPathsAsync(searchPath).ConfigureAwait(false);
        prioritySearchDirs.AddRange(pluginPaths);

        foreach (var dir in prioritySearchDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var skillDir in entries)
            {
                if (!HasSkillMd(skillDir))
                {
                    continue;
                }

                var skill = await ParseSkillMdAsync(
                    Path.Combine(skillDir, KnownConfigNames.SkillFileName),
                    options,
                    cancellationToken).ConfigureAwait(false);

                if (skill is null)
                {
                    continue;
                }

                var sanitized = NameSanitizer.SanitizeName(skill.Name);
                if (seenNames.Contains(sanitized))
                {
                    continue;
                }

                skills.Add(Enhance(skill, groupings));
                seenNames.Add(sanitized);
            }
        }

        if (skills.Count == 0 || options.FullDepth)
        {
            var allSkillDirs = new List<string>();
            CollectSkillDirs(searchPath, depth: 0, allSkillDirs, cancellationToken);

            foreach (var skillDir in allSkillDirs)
            {
                var skill = await ParseSkillMdAsync(
                    Path.Combine(skillDir, KnownConfigNames.SkillFileName),
                    options,
                    cancellationToken).ConfigureAwait(false);

                if (skill is null)
                {
                    continue;
                }

                var sanitized = NameSanitizer.SanitizeName(skill.Name);
                if (seenNames.Contains(sanitized))
                {
                    continue;
                }

                skills.Add(Enhance(skill, groupings));
                seenNames.Add(sanitized);
            }
        }

        return skills;
    }

    private static bool HasSkillMd(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, KnownConfigNames.SkillFileName));
        }
        catch
        {
            return false;
        }
    }

    private static Skill Enhance(Skill skill, Dictionary<string, string> groupings)
    {
        var resolved = Path.GetFullPath(skill.Path);
        if (groupings.TryGetValue(resolved, out var pluginName))
        {
            return skill with { PluginName = pluginName };
        }

        return skill;
    }

    private static void CollectSkillDirs(
        string dir,
        int depth,
        List<string> results,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (HasSkillMd(dir))
        {
            results.Add(dir);
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dir);
        }
        catch
        {
            return;
        }

        foreach (var subDir in subDirs)
        {
            var name = Path.GetFileName(subDir);
            if (SkipDirs.Contains(name))
            {
                continue;
            }

            CollectSkillDirs(subDir, depth + 1, results, cancellationToken);
        }
    }

    internal static async Task<Skill?> ParseSkillMdAsync(
        string skillMdPath,
        SkillDiscoveryOptions options,
        CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(skillMdPath, cancellationToken).ConfigureAwait(false);
        }
        catch
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

        var isInternal = metadata is not null
            && metadata.TryGetValue("internal", out var internalFlag)
            && internalFlag is string s
            && (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase));

        if (isInternal && !options.IncludeInternal && !ShouldInstallInternalSkills())
        {
            return null;
        }

        var skillDir = Path.GetDirectoryName(skillMdPath) ?? skillMdPath;

        return new Skill(
            Name: TerminalSanitizer.SanitizeMetadata(name),
            Description: TerminalSanitizer.SanitizeMetadata(description),
            Path: skillDir,
            RawContent: content,
            Metadata: metadata);
    }

    internal static bool ShouldInstallInternalSkills()
    {
        var env = Environment.GetEnvironmentVariable("INSTALL_INTERNAL_SKILLS");
        return env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }
}
