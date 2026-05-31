using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class AddCommandExecutor(
    ISourceParser sourceParser,
    IProviderRegistry providerRegistry,
    IInstaller installer,
    IInteractionService interaction,
    IAgentRegistry registry,
    IAgentEnvironmentDetector detector,
    IProjectLockFile projectLock,
    IGlobalLockFile globalLock,
    IAddCommandPrompter prompter,
    ConsoleEnvironment consoleEnvironment)
{
    public async Task<CommandResult> RunAsync(AddCommandOptions options, CancellationToken cancellationToken)
    {
        ParsedSource parsed;
        try
        {
            parsed = sourceParser.Parse(options.Source!);
        }
        catch (Exception ex)
        {
            interaction.WriteError($"Invalid source: {ex.Message}");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        var detection = await detector.DetectAgentAsync().ConfigureAwait(false);
        if (detection.IsAgent)
        {
            var agents = options.Agents;
            if (agents.Count == 0
                && detection.Name is { } name
                && detector.GetAgentType(name) is { } mapped)
            {
                agents = EnsureUniversalAgents([mapped]);
            }
            options = options with { Yes = true, Agents = agents };

            interaction.WriteMarkupLine(
                $"[on cyan] {Markup.Escape(detection.Name ?? "agent")} [/] Agent detected — installing non-interactively");
        }

        var skillFilters = MergeSkillFilters(options.SkillFilters, parsed);
        interaction.WriteDim($"Source: {GetSourceDisplayString(parsed)}");

        IReadOnlyList<RemoteSkill> skills;
        try
        {
            var provider = providerRegistry.Resolve(parsed);
            var providerOptions = new ProviderOptions(
                FullDepth: options.FullDepth,
                IncludeInternal: skillFilters.Count > 0);

            skills = await interaction
                .StatusAsync(
                    "Fetching skills...",
                    () =>
                        provider
                            .FetchSkillsAsync(parsed, providerOptions, cancellationToken)
                            .ContinueWith(t => (IReadOnlyList<RemoteSkill>)t.Result, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (CliException ex)
        {
            interaction.WriteError(ex.Message);
            return new CommandResult.Failure(ex.ExitCode);
        }
        catch (Exception ex)
        {
            var message =
                ex is AggregateException agg && agg.InnerException is not null
                    ? agg.InnerException.Message
                    : ex.Message;
            RenderErrorPanel(
                "Failed to fetch skills",
                message,
                "Tip: use the --yes (-y) and --global (-g) flags to install without prompts.");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        try
        {
            if (skills.Count == 0)
            {
                if (parsed is ParsedSource.WellKnown)
                {
                    interaction.WriteError(
                        "No skills found at this URL. Make sure the server has a /.well-known/agent-skills/index.json or /.well-known/skills/index.json file.");
                }
                else
                {
                    interaction.WriteError(
                        "No valid skills found. Skills require a SKILL.md with name and description.");
                }
                return new CommandResult.Failure(ExitCodeConstants.Failure);
            }

            interaction.WriteLine($"Found {skills.Count} skill(s)");

            if (options.List)
            {
                var filtered =
                    skillFilters.Count > 0 && !skillFilters.Contains("*", StringComparer.Ordinal)
                        ? skills
                            .Where(s =>
                                skillFilters.Any(f =>
                                    string.Equals(f, s.InstallName, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(f, s.Name, StringComparison.OrdinalIgnoreCase)
                                )
                            )
                            .ToList()
                        : skills;
                ListSkills(filtered);
                return new CommandResult.Success();
            }

            return await RunInstallationAsync(parsed, skills, skillFilters, options, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CleanupStagingPaths(skills);
        }
    }

    private async Task<CommandResult> RunInstallationAsync(
        ParsedSource parsed,
        IReadOnlyList<RemoteSkill> skills,
        IReadOnlyList<string> skillFilters,
        AddCommandOptions options,
        CancellationToken cancellationToken)
    {
        var selectedSkills = await SelectSkillsAsync(skills, skillFilters, options, cancellationToken)
            .ConfigureAwait(false);
        if (selectedSkills.Count == 0)
        {
            if (skillFilters.Count > 0)
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

        var nonInteractive = await IsNonInteractiveAsync(options).ConfigureAwait(false);

        var targetAgents = await SelectAgentsAsync(options, nonInteractive, cancellationToken).ConfigureAwait(false);
        if (targetAgents is null)
        {
            return new CommandResult.Cancelled();
        }

        if (targetAgents.Count == 0)
        {
            interaction.WriteError("No agents selected.");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        var installGlobally = options.Global;
        if (!options.Global && !nonInteractive)
        {
            var supportsGlobal = targetAgents.Any(a => registry.GetConfig(a).GlobalSkillsDir is not null);
            if (supportsGlobal)
            {
                installGlobally = await prompter.SelectGlobalScopeAsync(cancellationToken).ConfigureAwait(false);
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
            installMode = await prompter.SelectInstallModeAsync(cancellationToken).ConfigureAwait(false);
        }

        var overwriteTargets = GetOverwriteTargets(selectedSkills, installGlobally);

        if (!nonInteractive)
        {
            var confirmed = await prompter
                .ConfirmInstallationAsync(selectedSkills, targetAgents, overwriteTargets, cancellationToken)
                .ConfigureAwait(false);
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

        var results = await InstallSkillsAsync(selectedSkills, targetAgents, installOptions, cancellationToken)
            .ConfigureAwait(false);

        var successful = results.Where(r => r.Result.Success).ToList();
        var failed = results.Where(r => !r.Result.Success).ToList();

        if (successful.Count > 0)
        {
            await UpdateLocksAsync(parsed, successful, installGlobally, cancellationToken).ConfigureAwait(false);

            RenderInstallationSummary(targetAgents, successful, existingSkills, installGlobally);
        }

        if (failed.Count > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                failed.Select(r => $"{r.SkillName} → {r.AgentType}: {r.Result.Error}"));
            RenderErrorPanel("Installation failed", detail);
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        return new CommandResult.Success();
    }

    private void RenderInstallationSummary(
        IReadOnlyList<string> targetAgents,
        IReadOnlyList<InstallEntry> successful,
        HashSet<string> existingSkills,
        bool installGlobally)
    {
        var skillNames = successful.Select(r => r.SkillName).Distinct(StringComparer.Ordinal).ToList();
        var universals = targetAgents.Where(registry.IsUniversalAgent).ToList();
        var symlinked = targetAgents.Where(a => !registry.IsUniversalAgent(a)).ToList();
        var overwrites = skillNames.Where(existingSkills.Contains).ToList();

        var canonical =
            skillNames.Count == 1
                ? installer.GetCanonicalPath(skillNames[0], installGlobally)
                : installer.GetCanonicalSkillsDir(installGlobally);

        var summary = new System.Text.StringBuilder();
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
        interaction.Console.Write(
            new Panel(new Markup(summary.ToString().TrimEnd()))
                .Header("[bold]Installation Summary[/]")
                .BorderColor(Color.Cyan1)
                .Expand());

        var installed = new System.Text.StringBuilder();
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

        interaction.Console.Write(
            new Panel(new Markup(installed.ToString()))
                .Header($"[bold green]Installed {skillNames.Count} skill(s)[/]")
                .BorderColor(Color.Green)
                .Expand());

        interaction.WriteLine();
        interaction.WriteWarning("Done!  Review skills before use; they run with full agent permissions.");
    }

    private IReadOnlyList<OverwriteTarget> GetOverwriteTargets(
        IReadOnlyList<RemoteSkill> selectedSkills,
        bool installGlobally)
    {
        var overwrites = new List<OverwriteTarget>();
        foreach (var skill in selectedSkills)
        {
            var canonicalPath = installer.GetCanonicalPath(skill.InstallName, installGlobally);
            if (PathExists(canonicalPath))
            {
                overwrites.Add(new OverwriteTarget(skill.InstallName, canonicalPath));
            }
        }

        return overwrites;
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

    private void RenderErrorPanel(string title, string message, string? tip = null)
    {
        var content = new System.Text.StringBuilder();
        content.Append($"[red]{Markup.Escape(message)}[/]");
        if (tip is not null)
        {
            content.AppendLine();
            content.AppendLine();
            content.Append($"[dim]{Markup.Escape(tip)}[/]");
        }

        interaction.WriteLine();
        interaction.Console.Write(
            new Panel(new Markup(content.ToString()))
                .Header($"[bold red]{Markup.Escape(title)}[/]")
                .BorderColor(Color.Red)
                .Expand());
    }

    private static IReadOnlyList<string> MergeSkillFilters(IReadOnlyList<string> existing, ParsedSource parsed)
    {
        if (parsed is ParsedSource.GitHub github && !string.IsNullOrEmpty(github.SkillFilter))
        {
            if (!existing.Contains(github.SkillFilter, StringComparer.OrdinalIgnoreCase))
            {
                var merged = new List<string>(existing) { github.SkillFilter };
                return merged;
            }
        }

        return existing;
    }

    private void ListSkills(IReadOnlyList<RemoteSkill> skills)
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

    private async Task<IReadOnlyList<RemoteSkill>> SelectSkillsAsync(
        IReadOnlyList<RemoteSkill> skills,
        IReadOnlyList<string> skillFilters,
        AddCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (skillFilters.Count > 0)
        {
            if (skillFilters.Contains("*", StringComparer.Ordinal))
            {
                return skills;
            }

            return skills
                .Where(s =>
                    skillFilters.Any(f =>
                        string.Equals(f, s.InstallName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(f, s.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();
        }

        if (skills.Count == 1 || options.Yes)
        {
            return skills;
        }

        if (consoleEnvironment.IsInputRedirected)
        {
            interaction.WriteWarning("Installation cancelled");
            return Array.Empty<RemoteSkill>();
        }

        return await prompter.SelectSkillsAsync(skills, cancellationToken).ConfigureAwait(false);
    }

    private Task<bool> IsNonInteractiveAsync(AddCommandOptions options)
    {
        if (options.Yes || options.All)
        {
            return Task.FromResult(true);
        }

        if (consoleEnvironment.IsInputRedirected)
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private async Task<IReadOnlyList<string>?> SelectAgentsAsync(
        AddCommandOptions options,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        var validAgents = registry.ListAgentTypes();

        // --agent provided (incl. "*")
        if (options.Agents.Count > 0)
        {
            if (options.Agents.Contains("*", StringComparer.Ordinal))
            {
                return validAgents;
            }

            var invalid = options.Agents.Where(a => !validAgents.Contains(a)).ToList();
            if (invalid.Count > 0)
            {
                interaction.WriteError($"Invalid agents: {string.Join(", ", invalid)}");
                return Array.Empty<string>();
            }

            return options.Agents;
        }

        var installed = await detector.DetectInstalledAgentsAsync().ConfigureAwait(false);

        // Zero installed
        if (installed.Count == 0)
        {
            if (nonInteractive)
            {
                return registry.GetUniversalAgents();
            }

            return await prompter
                .SelectAgentsAsync(validAgents, options.Global, cancellationToken)
                .ConfigureAwait(false);
        }

        // One installed OR non-interactive → no prompt
        if (installed.Count == 1 || nonInteractive)
        {
            return EnsureUniversalAgents(installed);
        }

        // Multiple installed → prompt
        return await prompter.SelectAgentsAsync(validAgents, options.Global, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<string> EnsureUniversalAgents(IReadOnlyList<string> agents)
    {
        var universal = registry.GetUniversalAgents();
        var result = new List<string>(agents);
        foreach (var u in universal)
        {
            if (!result.Contains(u, StringComparer.Ordinal))
            {
                result.Add(u);
            }
        }

        return result;
    }

    private static void CleanupStagingPaths(IEnumerable<RemoteSkill> skills)
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

    private async Task<List<InstallEntry>> InstallSkillsAsync(
        IReadOnlyList<RemoteSkill> selectedSkills,
        IReadOnlyList<string> targetAgents,
        InstallOptions installOptions,
        CancellationToken cancellationToken)
    {
        var results = new List<InstallEntry>();
        await interaction
            .StatusAsync("Installing skills...", async () => { foreach (var skill in selectedSkills)
                {
                    foreach (var agentType in targetAgents)
                    {
                        var result = await installer
                            .InstallRemoteSkillForAgentAsync(skill, agentType, installOptions, cancellationToken)
                            .ConfigureAwait(false);
                        results.Add(new InstallEntry(skill, agentType, result));
                    }
                } })
            .ConfigureAwait(false);

        return results;
    }

    private async Task UpdateLocksAsync(
        ParsedSource parsed,
        IReadOnlyList<InstallEntry> successful,
        bool installGlobally,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.ToString("o");
        var sourceUrl = GetSourceUrl(parsed);
        var sourceType = GetSourceType(parsed);
        var refValue = GetSourceRef(parsed);
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
                var skillFolderHash = string.Empty;
                try
                {
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        skillFolderHash = await projectLock
                            .ComputeSkillFolderHashAsync(installPath, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch { }

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
                        await globalLock
                            .AddEntryAsync(entry.Skill.InstallName, lockEntry, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            else
            {
                var computedHash = string.Empty;
                try
                {
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        computedHash = await projectLock
                            .ComputeSkillFolderHashAsync(installPath, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch { }

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
                    await projectLock
                        .AddEntryAsync(entry.Skill.InstallName, lockEntry, cwd: null, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    private static string GetSourceUrl(ParsedSource source)
        => source switch
        {
            ParsedSource.GitHub gh => gh.Url,
            ParsedSource.GitLab gl => gl.Url,
            ParsedSource.Git g => g.Url,
            ParsedSource.Local l => l.Url,
            ParsedSource.WellKnown w => w.Url,
            _ => string.Empty
        };

    private static string GetSourceType(ParsedSource source)
        => source switch
        {
            ParsedSource.GitHub => "github",
            ParsedSource.GitLab => "gitlab",
            ParsedSource.Git => "git",
            ParsedSource.Local => "local",
            ParsedSource.WellKnown => "well-known",
            _ => "unknown"
        };

    private static string? GetSourceRef(ParsedSource source)
        => source switch
        {
            ParsedSource.GitHub gh => gh.Ref,
            ParsedSource.GitLab gl => gl.Ref,
            ParsedSource.Git g => g.Ref,
            _ => null
        };

    private static string GetSourceDisplayString(ParsedSource source)
    {
        var url = GetSourceUrl(source);
        var @ref = GetSourceRef(source);
        if (!string.IsNullOrEmpty(@ref))
        {
            url += $" @ {@ref}";
        }
        if (source is ParsedSource.GitHub gh && !string.IsNullOrEmpty(gh.Subpath))
        {
            url += $" ({gh.Subpath})";
        }
        return url;
    }

    private sealed record InstallEntry(RemoteSkill Skill, string AgentType, InstallResult Result)
    {
        public string SkillName => Skill.InstallName;
    }
}
