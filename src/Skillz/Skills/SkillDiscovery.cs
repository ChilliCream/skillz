using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Skillz.Install;
using Skillz.Paths;
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
    PluginManifest pluginManifest,
    PluginGrouping pluginGrouping,
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
        // SafePath.Contains rejects any ".." segment before resolving, so the reject
        // happens before any containment resolution.
        if (subpath is not null
            && !SafePath.Contains(basePath, Path.Combine(basePath, subpath), LeafPolicy.Follow))
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

            var skill = await ParseSkillMdAsync(dir, cancellationToken);

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
            foreach (var dir in EnumerateCrawlSkillDirs(searchPath, cancellationToken))
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

            foreach (var dir in EnumerateContainedChildDirs(priorityRoot, cancellationToken))
            {
                yield return dir;
            }
        }
    }

    /// <summary>
    /// Yields the immediate child directories of <paramref name="root"/> through a contained
    /// walk so that a directory whose real path escapes <paramref name="root"/> - for example a
    /// planted in-tree symlink to <c>~/.ssh</c> - resolves out of root and is never yielded.
    /// Returns nothing when the root is missing or unresolvable.
    /// </summary>
    private IEnumerable<string> EnumerateContainedChildDirs(string root, CancellationToken cancellationToken)
    {
        return EnumerateContainedDirs(root, maxRelativeDepth: 1, skipNames: null, cancellationToken)
            // filter out the root as this is emmited at depth 0
            .Where(dir =>
            {
                if (SafePath.PathEquals(dir, root))
                {
                    return true;
                }

                var dirReal = SafePath.ResolveExisting(dir);
                var rootReal = SafePath.ResolveExisting(root);
                return dirReal is not null && rootReal is not null && SafePath.PathEquals(dirReal, rootReal);
            });
    }

    /// <summary>
    /// Yields <paramref name="dir"/> and every directory beneath it - up to
    /// <see cref="MaxDepth"/> levels deep and skipping <see cref="s_skipDirs"/> - through a
    /// contained walk, so a directory whose real path escapes <paramref name="dir"/> (e.g. a
    /// planted in-tree symlink to <c>~/.ssh</c>) is never yielded.
    /// </summary>
    private IEnumerable<string> EnumerateCrawlSkillDirs(string dir, CancellationToken cancellationToken)
        => EnumerateContainedDirs(dir, maxRelativeDepth: MaxDepth, skipNames: s_skipDirs, cancellationToken);

    /// <summary>
    /// Contained directory enumeration shared by the priority and crawl paths. Walks
    /// <paramref name="root"/> with <see cref="OnSymlink.FollowIfContained"/> so a child whose
    /// real path escapes <paramref name="root"/> is dropped rather than descended into, yields
    /// only directory entries within <paramref name="maxRelativeDepth"/> logical levels of the
    /// root, and stops cleanly if the walk refuses (e.g. the root is unresolvable or a
    /// pathological depth is hit). The walk's own depth bound is the cyclic-symlink guard; the
    /// relative-depth filter bounds skill-finding without throwing on a legitimately deep real
    /// tree.
    /// </summary>
    private IEnumerable<string> EnumerateContainedDirs(
        string root,
        int maxRelativeDepth,
        IReadOnlySet<string>? skipNames,
        CancellationToken cancellationToken)
    {
        if (!fileStore.DirectoryExists(root))
        {
            yield break;
        }

        var options = WalkOptions.ContainedTo(root, OnSymlink.FollowIfContained, skip: skipNames);

        foreach (var entry in fileStore.WalkBestEffort(root, options, cancellationToken))
        {
            if (entry.Kind != WalkEntryKind.Directory)
            {
                continue;
            }

            if (RelativeDepth(root, entry.LogicalPath) <= maxRelativeDepth)
            {
                yield return entry.LogicalPath;
            }
        }
    }

    private static int RelativeDepth(string root, string descendant)
    {
        var relative = Path.GetRelativePath(root, descendant);
        if (relative.Length == 0 || relative.EqualsOrdinal("."))
        {
            return 0;
        }

        var depth = 1;
        foreach (var ch in relative)
        {
            if (ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar)
            {
                depth++;
            }
        }

        return depth;
    }

    /// <summary>
    /// Reads and parses the <c>SKILL.md</c> in <paramref name="skillDir"/> into a
    /// <see cref="Skill"/>. Parsing is policy-free; the caller decides whether to keep the
    /// result (e.g. internal-skill filtering).
    /// </summary>
    /// <param name="skillDir">The directory holding the <c>SKILL.md</c>; also the containment root.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>
    /// The parsed skill, or <see langword="null"/> when the file is unreadable, has invalid
    /// frontmatter, is missing a non-empty <c>name</c>/<c>description</c>, or is a symlinked leaf
    /// pointing outside <paramref name="skillDir"/> (such a leaf is refused, never dereferenced
    /// into <see cref="Skill.RawContent"/>). Name and description are run through
    /// <see cref="TerminalSanitizer"/> to neutralize untrusted terminal escapes.
    /// </returns>
    private async Task<Skill?> ParseSkillMdAsync(string skillDir, CancellationToken cancellationToken)
    {
        var skillMdPath = Path.Combine(skillDir, KnownConfigNames.SkillFileName);

        string content;
        try
        {
            // No-follow read contained against the skill directory: a SKILL.md whose leaf
            // is a symlink to an out-of-tree file is refused rather than read through, so secret
            // bytes never enter RawContent.
            content = await fileStore.ReadAllTextNoFollowAsync(skillMdPath, skillDir, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CliException)
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

        // Sanitize before the empty-check: a name/description of pure escape bytes is non-empty here
        // but collapses to "" once neutralized, so it must be rejected against the sanitized value.
        var sanitizedName = TerminalSanitizer.SanitizeMetadata(name);
        var sanitizedDescription = TerminalSanitizer.SanitizeMetadata(description);

        if (string.IsNullOrEmpty(sanitizedName) || string.IsNullOrEmpty(sanitizedDescription))
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

        // Reject (do not sanitize) a directory containing control bytes: Skill.Path must stay
        // byte-exact for file I/O, so we skip the skill rather than rewrite the path.
        if (skillDir.ContainsControlCharacter())
        {
            return null;
        }

        return new Skill(
            Name: sanitizedName,
            Description: sanitizedDescription,
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

    /// <summary>
    /// Walks <paramref name="root"/> like <see cref="IFileStore.Walk"/>, but treats a
    /// <see cref="CliException"/> as "stop here" rather than propagating. A refusal raised while
    /// starting the walk (root missing/unresolvable or escaping containment — it throws eagerly,
    /// because <see cref="IFileStore.Walk"/> passes straight through to <see cref="SafeTreeWalker.Walk"/>)
    /// yields an empty sequence; a refusal raised mid-walk (the cyclic-symlink depth bound)
    /// truncates the sequence after the entries already produced. Cancellation is NOT suppressed.
    /// Discovery is best-effort, so a pathological corner of the tree degrades the result instead
    /// of aborting the command.
    /// </summary>
    public static IEnumerable<WalkEntry> WalkBestEffort(
        this IFileStore fileStore,
        string root,
        WalkOptions options,
        CancellationToken cancellationToken)
    {
        IEnumerator<WalkEntry> enumerator;
        try
        {
            enumerator = fileStore.Walk(root, options, cancellationToken).GetEnumerator();
        }
        catch (CliException)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (CliException)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
