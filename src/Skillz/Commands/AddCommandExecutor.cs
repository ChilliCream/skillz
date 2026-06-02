using System.Collections.Immutable;
using System.Text;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Commands;

internal sealed class AddCommandExecutor(
    ISourceParser sourceParser,
    ProviderRegistry providerRegistry,
    ISkillInstaller installer,
    IInteractionService interaction,
    AgentRegistry registry,
    IAgentEnvironmentDetector detector,
    IProjectLockFile projectLock,
    IGlobalLockFile globalLock,
    IAddCommandPrompter prompter,
    ConsoleEnvironment consoleEnvironment)
{
    public async Task<CommandResult> RunAsync(AddCommandOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(options, cancellationToken);
        }
        catch (CliException ex)
        {
            if (ex.Title is { } title)
            {
                interaction.WriteErrorPanel(title, ex.Message, ex.Hint);
            }
            else
            {
                interaction.WriteError(ex.Message);
            }

            return new CommandResult.Failure(ex.ExitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            interaction.WriteError(ex.Message);
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }
    }

    private async Task<CommandResult> RunCoreAsync(AddCommandOptions options, CancellationToken cancellationToken)
    {
        // Resolve the source argument (owner/repo, URL, or local path) into a typed source.
        var parsed = ParseSource(options.Source!);

        // When invoked by an agent (e.g. Claude Code), switch to a non-interactive install for it.
        options = ApplyAgentContext(options);

        // Combine any inline source filter (e.g. owner/repo#skill) with the --skill options.
        var skillFilters = MergeSkillFilters(options.SkillFilters, parsed);
        interaction.WriteDim($"Source: {parsed.DisplayString}");

        // Fetch skills into a temp staging area, and clean it up no matter how we exit below.
        var skills = await FetchSkillsAsync(parsed, options, skillFilters, cancellationToken);
        try
        {
            // Turn the fetched skills into the final outcome: fail if none were found, print a
            // plain listing for --list, or otherwise run the install.
            if (skills.Length == 0)
            {
                throw new CliException(
                    ExitCodeConstants.Failure,
                    parsed is SkillSource.WellKnown
                        ? "No skills found at this URL. Make sure the server has a /.well-known/agent-skills/index.json or /.well-known/skills/index.json file."
                        : "No valid skills found. Skills require a SKILL.md with name and description.");
            }

            interaction.WriteLine($"Found {skills.Length} skill(s)");

            if (options.List)
            {
                ListSkills(ApplyFilters(skills, skillFilters));
                return new CommandResult.Success();
            }

            return await RunInstallationAsync(parsed, skills, skillFilters, options, cancellationToken);
        }
        finally
        {
            CleanupStagingPaths(skills);
        }
    }

    // Adapts the run for an agent host: when one is detected, default the target to that agent
    // (plus the universal agents) and skip every prompt, since an agent can't answer them.
    private AddCommandOptions ApplyAgentContext(AddCommandOptions options)
    {
        var detection = detector.DetectAgent;

        if (!detection.IsAgent)
        {
            return options;
        }

        var agents = options.Agents;

        if (agents.Length == 0
            && detection.Name is { } name
            && detector.GetAgentType(name) is { } mapped)
        {
            agents = EnsureUniversalAgents([mapped]);
        }

        interaction.WriteMarkupLine(
            $"[on cyan] {Markup.Escape(detection.Name ?? "agent")} [/] Agent detected — installing non-interactively");

        return options with { Yes = true, Agents = agents };
    }

    private SkillSource ParseSource(string source)
    {
        try
        {
            return sourceParser.Parse(source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CliException(ExitCodeConstants.Failure, $"Invalid source: {ex.Message}");
        }
    }

    private async Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource parsed,
        AddCommandOptions options,
        ImmutableArray<string> skillFilters,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = providerRegistry.Resolve(parsed);

            var providerOptions = new ProviderOptions(options.FullDepth, skillFilters.Length > 0);

            return await interaction.StatusAsync(
                "Fetching skills...",
                async () => await provider.FetchSkillsAsync(parsed, providerOptions, cancellationToken));
        }
        catch (Exception ex) when (ex is not CliException and not OperationCanceledException)
        {
            // Reframe arbitrary fetch failures (network, parsing, …) as a presented CliException;
            // CliException and cancellation pass through untouched and are handled at the boundary.
            throw new CliException(
                ExitCodeConstants.Failure,
                ex.Message,
                title: "Failed to fetch skills",
                hint: "Tip: use the --yes (-y) and --global (-g) flags to install without prompts.");
        }
    }

    private async Task<CommandResult> RunInstallationAsync(
        SkillSource parsed,
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> skillFilters,
        AddCommandOptions options,
        CancellationToken cancellationToken)
    {
        var selectedSkills = await SelectSkillsAsync(skills, skillFilters, options, cancellationToken);
        if (selectedSkills.Length == 0)
        {
            if (skillFilters.Length > 0)
            {
                interaction.WriteError($"No matching skills found for: {string.Join(", ", skillFilters)}");
                interaction.WriteLine("Available skills:");
                foreach (var s in skills)
                {
                    interaction.WriteLine($"  {s.InstallName}");
                }
                return new CommandResult.Failure(ExitCodeConstants.Failure);
            }
            interaction.WriteWarning("Installation cancelled");
            return new CommandResult.Cancelled();
        }

        var nonInteractive = IsNonInteractive(options);

        var selectedAgents = await SelectAgentsAsync(options, nonInteractive, cancellationToken);
        if (selectedAgents is not { } targetAgents)
        {
            return new CommandResult.Cancelled();
        }

        if (targetAgents.Length == 0)
        {
            throw new CliException(ExitCodeConstants.Failure, "No agents selected.");
        }

        var installGlobally = options.Global;
        if (!options.Global && !nonInteractive)
        {
            var supportsGlobal = targetAgents.Any(a => registry.GetConfig(a).GlobalSkillsDir is not null);
            if (supportsGlobal)
            {
                installGlobally = await prompter.SelectGlobalScopeAsync(cancellationToken);
            }
        }

        var installMode = options.Copy ? InstallMode.Copy : InstallMode.Symlink;
        var uniqueDirs = new HashSet<string>(
            targetAgents.Select(a => registry.GetConfig(a).SkillsDir),
            StringComparer.Ordinal);

        if (uniqueDirs.Count <= 1)
        {
            installMode = InstallMode.Copy;
        }
        else if (!options.Copy && !nonInteractive)
        {
            installMode = await prompter.SelectInstallModeAsync(cancellationToken);
        }

        var overwriteTargets = GetOverwriteTargets(selectedSkills, installGlobally);

        if (!nonInteractive)
        {
            var confirmed = await prompter.ConfirmInstallationAsync(
                selectedSkills,
                targetAgents,
                overwriteTargets,
                cancellationToken);
            if (!confirmed)
            {
                interaction.WriteWarning("Installation cancelled");
                return new CommandResult.Cancelled();
            }
        }

        var installOptions = new InstallOptions(installGlobally, Cwd: null, installMode);
        var existingSkills = overwriteTargets.Select(o => o.SkillName).ToHashSet(StringComparer.Ordinal);
        if (nonInteractive)
        {
            foreach (var overwrite in overwriteTargets)
            {
                interaction.WriteWarning(
                    $"Overwriting existing skill '{overwrite.SkillName}' at {overwrite.DestinationPath}");
            }
        }

        var results = await InstallSkillsAsync(selectedSkills, targetAgents, installOptions, cancellationToken);

        var successful = results.Where(r => r.Result.Success).ToImmutableArray();
        var failed = results.Where(r => !r.Result.Success).ToImmutableArray();

        if (successful.Length > 0)
        {
            await UpdateLocksAsync(parsed, successful, installGlobally, cancellationToken);

            RenderInstallationSummary(targetAgents, successful, existingSkills, installGlobally);
        }

        if (failed.Length > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                failed.Select(r => $"{r.SkillName} → {r.AgentType}: {r.Result.Error}"));
            throw new CliException(ExitCodeConstants.Failure, detail, title: "Installation failed");
        }

        return new CommandResult.Success();
    }

    private void RenderInstallationSummary(
        ImmutableArray<string> targetAgents,
        ImmutableArray<InstallEntry> successful,
        HashSet<string> existingSkills,
        bool installGlobally)
    {
        var skillNames = successful.Select(r => r.SkillName).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var universals = targetAgents.Where(registry.IsUniversalAgent).ToList();
        var symlinked = targetAgents.Where(a => !registry.IsUniversalAgent(a)).ToList();
        var overwrites = skillNames.Where(existingSkills.Contains).ToList();

        var canonical =
            skillNames.Length == 1
                ? installer.GetCanonicalPath(skillNames[0], installGlobally)
                : installer.GetCanonicalSkillsDir(installGlobally);

        var summary = new StringBuilder();
        summary.AppendLine($"[bold]Canonical:[/] [dim]{Markup.Escape(canonical)}[/]");
        if (universals.Count > 0)
        {
            summary.AppendLine(
                $"[bold]Universal:[/]  {Markup.Escape(string.Join(", ", universals.Select(GetAgentDisplay)))}");
        }
        if (symlinked.Count > 0)
        {
            summary.AppendLine(
                $"[bold]Symlinked:[/]  {Markup.Escape(string.Join(", ", symlinked.Select(GetAgentDisplay)))}");
        }
        if (overwrites.Count > 0)
        {
            summary.Append($"[yellow]Overwrites:[/] {Markup.Escape(string.Join(", ", overwrites))}");
        }

        interaction.WriteLine();
        interaction.WriteRenderable(
            new Panel(new Markup(summary.ToString().TrimEnd()))
                .Header("[bold]Installation Summary[/]")
                .BorderColor(Color.Cyan1)
                .Expand());

        var installed = new StringBuilder();
        foreach (var skillName in skillNames)
        {
            var paths = successful
                .Where(r => r.SkillName == skillName)
                .Select(r => r.Result.Path)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var firstPath = paths.FirstOrDefault() ?? string.Empty;
            installed.AppendLine($"[green]✓[/] {Markup.Escape(skillName)}");
            installed.Append($"  [dim]→ {Markup.Escape(firstPath)}[/]");
            if (skillName != skillNames[^1])
            {
                installed.AppendLine();
            }
        }

        interaction.WriteRenderable(
            new Panel(new Markup(installed.ToString()))
                .Header($"[bold green]Installed {skillNames.Length} skill(s)[/]")
                .BorderColor(Color.Green)
                .Expand());

        interaction.WriteLine();
        interaction.WriteWarning("Done!  Review skills before use; they run with full agent permissions.");
    }

    private ImmutableArray<OverwriteTarget> GetOverwriteTargets(
        ImmutableArray<ResolvedSkill> selectedSkills,
        bool installGlobally)
    {
        var overwrites = ImmutableArray.CreateBuilder<OverwriteTarget>();
        foreach (var skill in selectedSkills)
        {
            var canonicalPath = installer.GetCanonicalPath(skill.InstallName, installGlobally);
            if (PathExists(canonicalPath))
            {
                overwrites.Add(new OverwriteTarget(skill.InstallName, canonicalPath));
            }
        }

        return overwrites.ToImmutable();
    }

    private static bool PathExists(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private string GetAgentDisplay(string agentType)
    {
        var config = registry.GetConfig(agentType);
        return config.DisplayName;
    }

    private static ImmutableArray<string> MergeSkillFilters(ImmutableArray<string> existing, SkillSource parsed)
    {
        if (parsed is SkillSource.ISkillFilterable filterable
            && !string.IsNullOrEmpty(filterable.SkillFilter))
        {
            if (!existing.Contains(filterable.SkillFilter, StringComparer.OrdinalIgnoreCase))
            {
                return [.. existing, filterable.SkillFilter];
            }
        }

        return [.. existing];
    }

    private void ListSkills(ImmutableArray<ResolvedSkill> skills)
    {
        interaction.WriteLine();
        interaction.WriteMarkupLine("[bold]Available Skills[/]");

        var groups = skills
            .GroupBy(s => s.PluginName, StringComparer.Ordinal)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        var first = true;
        foreach (var group in groups)
        {
            if (group.Key is { } pluginName)
            {
                if (!first)
                {
                    interaction.WriteLine();
                }
                interaction.WriteMarkupLine($"[bold]{Markup.Escape(TitleCase(pluginName))}[/]");
            }

            foreach (var skill in group.OrderBy(s => s.InstallName, StringComparer.Ordinal))
            {
                interaction.WriteMarkupLine($"  [cyan]{Markup.Escape(skill.InstallName)}[/]");
                interaction.WriteDim($"    {skill.Description}");
            }

            first = false;
        }

        interaction.WriteLine();
        interaction.WriteDim("Use --skill <name> to install specific skills");
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var parts = value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private async Task<ImmutableArray<ResolvedSkill>> SelectSkillsAsync(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> skillFilters,
        AddCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (skillFilters.Length > 0)
        {
            return ApplyFilters(skills, skillFilters);
        }

        if (skills.Length == 1 || options.Yes)
        {
            return skills;
        }

        if (consoleEnvironment.IsInputRedirected)
        {
            interaction.WriteWarning("Installation cancelled");
            return ImmutableArray<ResolvedSkill>.Empty;
        }

        return await prompter.SelectSkillsAsync(skills, cancellationToken);
    }

    private bool IsNonInteractive(AddCommandOptions options)
        => options.Yes || options.All || consoleEnvironment.IsInputRedirected;

    private async Task<ImmutableArray<string>?> SelectAgentsAsync(
        AddCommandOptions options,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        var validAgents = registry.AgentTypes;

        // --agent provided (incl. "*")
        if (options.Agents.Length > 0)
        {
            if (options.Agents.Contains("*", StringComparer.Ordinal))
            {
                return validAgents;
            }

            var invalid = options.Agents.Where(a => !validAgents.Contains(a)).ToList();
            if (invalid.Count > 0)
            {
                interaction.WriteError($"Invalid agents: {string.Join(", ", invalid)}");
                return ImmutableArray<string>.Empty;
            }

            return options.Agents;
        }

        var installed = detector.DetectInstalledAgents();

        // Zero installed
        if (installed.Length == 0)
        {
            if (nonInteractive)
            {
                return registry.UniversalAgents;
            }

            return await prompter.SelectAgentsAsync(validAgents, options.Global, cancellationToken);
        }

        // One installed OR non-interactive → no prompt
        if (installed.Length == 1 || nonInteractive)
        {
            return EnsureUniversalAgents(installed);
        }

        // Multiple installed → prompt
        return await prompter.SelectAgentsAsync(validAgents, options.Global, cancellationToken);
    }

    private ImmutableArray<string> EnsureUniversalAgents(IReadOnlyList<string> agents)
    {
        var universal = registry.UniversalAgents;
        var result = new List<string>(agents);
        foreach (var u in universal)
        {
            if (!result.Contains(u, StringComparer.Ordinal))
            {
                result.Add(u);
            }
        }

        return [.. result];
    }

    private static void CleanupStagingPaths(IEnumerable<ResolvedSkill> skills)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            if (string.IsNullOrEmpty(skill.CleanupPath))
            {
                continue;
            }

            if (!seen.Add(skill.CleanupPath))
            {
                continue;
            }

            TempDirCleanup.SafeDelete(skill.CleanupPath);
        }
    }

    private async Task<ImmutableArray<InstallEntry>> InstallSkillsAsync(
        ImmutableArray<ResolvedSkill> selectedSkills,
        ImmutableArray<string> targetAgents,
        InstallOptions installOptions,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<InstallEntry>();
        await interaction.StatusAsync("Installing skills...", async () => { foreach (var skill in selectedSkills)
            {
                foreach (var agentType in targetAgents)
                {
                    var result = await installer.InstallAsync(skill, agentType, installOptions, cancellationToken);
                    results.Add(new InstallEntry(skill, agentType, result));
                }
            } });

        return results.ToImmutable();
    }

    private async Task UpdateLocksAsync(
        SkillSource parsed,
        ImmutableArray<InstallEntry> successful,
        bool installGlobally,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.ToString("o");
        var sourceUrl = parsed.Url;
        var sourceType = parsed.SourceType;
        var refValue = parsed.Ref;
        var ownerRepo = OwnerRepoParser.GetOwnerRepo(parsed);
        var source = ownerRepo ?? sourceUrl;

        var bySkill = successful
            .GroupBy(r => r.Skill.InstallName, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        foreach (var entry in bySkill)
        {
            var installPath = entry.Result.Path;
            if (installGlobally)
            {
                var skillFolderHash = await ComputeHashSafeAsync(installPath, cancellationToken);

                var lockEntry = new SkillLockEntry
                {
                    Source = source,
                    SourceType = sourceType,
                    SourceUrl = sourceUrl,
                    Ref = refValue,
                    SkillPath = entry.Skill.SkillPath,
                    SkillFolderHash = skillFolderHash,
                    InstalledAt = now,
                    UpdatedAt = now
                };
                if (!string.IsNullOrEmpty(ownerRepo))
                {
                    try
                    {
                        await globalLock.AddEntryAsync(entry.Skill.InstallName, lockEntry, cancellationToken);
                    }
                    catch { }
                }
            }
            else
            {
                var computedHash = await ComputeHashSafeAsync(installPath, cancellationToken);

                var lockEntry = new LocalSkillLockEntry
                {
                    Source = source,
                    SourceType = sourceType,
                    Ref = refValue,
                    SkillPath = entry.Skill.SkillPath,
                    ComputedHash = computedHash
                };
                try
                {
                    await projectLock.AddEntryAsync(entry.Skill.InstallName, lockEntry, cwd: null, cancellationToken);
                }
                catch { }
            }
        }
    }

    private static ImmutableArray<ResolvedSkill> ApplyFilters(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> filters)
    {
        if (filters.Length == 0 || filters.Contains("*", StringComparer.Ordinal))
        {
            return skills;
        }

        return skills
            .Where(s =>
                filters.Any(f =>
                    string.Equals(f, s.InstallName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f, s.Name, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToImmutableArray();
    }

    private async Task<string> ComputeHashSafeAsync(string? path, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                return await projectLock.ComputeSkillFolderHashAsync(path, cancellationToken);
            }
        }
        catch
        {
            // Hash is advisory; on failure we fall back to an empty hash.
        }

        return string.Empty;
    }

    private sealed record InstallEntry(ResolvedSkill Skill, string AgentType, InstallResult Result)
    {
        public string SkillName => Skill.InstallName;
    }
}
