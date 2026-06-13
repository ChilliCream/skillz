using System.Collections.Immutable;
using System.CommandLine;
using Skillz.Commands.Selection;
using Skillz.Git;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Skillz.Interaction.Prompts;
using Skillz.Locking;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Skillz.Utils;
using Skillz.Views;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class AddCommand(
    IAnsiConsole console,
    ISourceParser sourceParser,
    ProviderRegistry providerRegistry,
    ISkillInstaller installer,
    InstallRecorder recorder,
    AgentRegistry registry,
    AgentEnvironment agentEnvironment,
    IGlobalLockFile globalLock,
    ISkillSelector skillSelector,
    IAgentSelector agentSelector,
    IFileStore fileStore,
    ConsoleEnvironment consoleEnvironment)
    : BaseCommand(console, "add", "Add a skill from a source")
{
    private static readonly ImmutableArray<(string Label, InstallMode Value)> s_installModeChoices =
    [
        ("Symlink (Recommended)", InstallMode.Symlink),
        ("Copy to all agents", InstallMode.Copy)
    ];

    private static readonly ImmutableArray<(string Label, bool Value)> s_globalScopeChoices =
    [
        ("Project (install in current directory)", false),
        ("Global (install in home directory)", true)
    ];

    private static readonly ImmutableHashSet<string> s_defaultSelection =
        ImmutableHashSet.Create(StringComparer.Ordinal, "claude-code", "opencode", "codex");

    private readonly Argument<string?> _sourceArgument = new("source")
    {
        Description = "Source to fetch skills from (e.g., owner/repo, URL, local path)",
        Arity = ArgumentArity.ZeroOrOne
    };

    private readonly Option<bool> _globalOption = new(CommonOptionNames.Global, "-g")
    {
        Description = "Install globally"
    };

    private readonly Option<string[]> _agentOption = new(CommonOptionNames.Agent, "-a")
    {
        Description = "Target agent(s)",
        AllowMultipleArgumentsPerToken = true
    };

    private readonly Option<string[]> _skillOption = new(CommonOptionNames.Skill, "-s")
    {
        Description = "Skill name filter(s)",
        AllowMultipleArgumentsPerToken = true
    };

    private readonly Option<bool> _yesOption = new(CommonOptionNames.Yes, "-y")
    {
        Description = "Skip prompts (non-interactive)"
    };

    private readonly Option<bool> _allOption = new(CommonOptionNames.All)
    {
        Description = "Install all skills to all agents"
    };

    private readonly Option<bool> _copyOption = new(CommonOptionNames.Copy)
    {
        Description = "Copy instead of symlinking"
    };

    private readonly Option<bool> _fullDepthOption = new(CommonOptionNames.FullDepth)
    {
        Description = "Full-depth clone"
    };

    private readonly Option<bool> _listOption = new(CommonOptionNames.List, "-l")
    {
        Description = "List available skills without installing"
    };

    protected override void Configure()
    {
        Arguments.Add(_sourceArgument);
        Options.Add(_globalOption);
        Options.Add(_agentOption);
        Options.Add(_skillOption);
        Options.Add(_yesOption);
        Options.Add(_allOption);
        Options.Add(_copyOption);
        Options.Add(_fullDepthOption);
        Options.Add(_listOption);
    }

    protected override async Task<int> ExecuteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        var options = ParseOptions(parseResult);

        if (string.IsNullOrWhiteSpace(options.Source))
        {
            Output.Error("Missing required argument: source");
            Output.WriteLine("Usage: skillz add <source> [options]");
            return ExitCodeConstants.Failure;
        }

        // Resolve the source argument (owner/repo, URL, or local path) into a typed source.
        var parsed = sourceParser.Parse(options.Source);

        // Adapts the run for an agent host: when one is detected, default the target to that agent
        // (plus the universal agents) and skip every prompt, since an agent can't answer them.
        if (agentEnvironment.CurrentAgentName is { } agentName)
        {
            var agents = options.Agents;

            if (agents.Length == 0 && agentEnvironment.FindAgentType(agentName) is { } mapped)
            {
                agents = registry.UniversalAgents.Append(mapped).Distinct(StringComparer.Ordinal).ToImmutableArray();
            }

            Output.MarkupLineRaw(
                $"[on cyan] {Markup.Escape(agentName)} [/] Agent detected - installing non-interactively");

            options = options with { Yes = true, Agents = agents };
        }

        var skillFilters = options.SkillFilters.AppendFilter(parsed.GetSkillFilter());

        Output.Dim(
            $"Source: {TerminalSanitizer.SanitizeMetadata(GitUrl.RedactUrlUserInfo(parsed.DisplayString))}");

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

            Output.WriteLine($"Found {skills.Length} skill(s)");

            if (options.List)
            {
                Output.WriteLine();
                Output.Write(SkillListView.Create(skills.ApplyFilters(skillFilters)));
                return ExitCodeConstants.Success;
            }

            return await RunInstallationAsync(parsed, skills, skillFilters, options, cancellationToken);
        }
        finally
        {
            CleanupStagingPaths(skills);
        }
    }

    private AddCommandOptions ParseOptions(ParseResult parseResult)
    {
        var source = parseResult.GetValue(_sourceArgument);
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? [];
        var skills = parseResult.GetValue(_skillOption) ?? [];
        var yes = parseResult.GetValue(_yesOption);
        var all = parseResult.GetValue(_allOption);
        var copy = parseResult.GetValue(_copyOption);
        var fullDepth = parseResult.GetValue(_fullDepthOption);
        var list = parseResult.GetValue(_listOption);

        if (all)
        {
            skills = ["*"];
            agents = ["*"];
            yes = true;
        }

        return new AddCommandOptions(source, global, [.. agents], [.. skills], yes, all, copy, fullDepth, list);
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

            return await Output.StatusAsync(
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

    private async Task<int> RunInstallationAsync(
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
                Output.Error($"No matching skills found for: {skillFilters.Join(", ")}");
                Output.WriteLine("Available skills:");
                foreach (var s in skills)
                {
                    Output.WriteLine($"  {s.InstallName}");
                }
                return ExitCodeConstants.Failure;
            }
            Output.Warning("Installation cancelled");
            return ExitCodeConstants.Cancelled;
        }

        var nonInteractive = options.Yes || options.All || consoleEnvironment.IsInputRedirected;

        var selectedAgents = await SelectAgentsAsync(options, nonInteractive, cancellationToken);
        if (selectedAgents is not { } targetAgents)
        {
            return ExitCodeConstants.Cancelled;
        }

        if (targetAgents.Length == 0)
        {
            throw new CliException(ExitCodeConstants.Failure, "No agents selected.");
        }

        var installGlobally = options.Global;
        if (!options.Global && !nonInteractive)
        {
            var supportsGlobal = targetAgents.Any(a => registry.GetConfig(a).GlobalSkillsDirectory is not null);
            if (supportsGlobal)
            {
                // WithDefault keeps the project scope when the console cannot show the picker (redirected
                // or non-ANSI), matching the default taken when the prompt is skipped entirely.
                installGlobally = await new SelectPrompt<bool>("Installation scope", s_globalScopeChoices)
                    .WithDefault(defaultValue: false)
                    .ShowAsync(Output, cancellationToken);
            }
        }

        var installMode = options.Copy ? InstallMode.Copy : InstallMode.Symlink;
        var hasMultipleSkillsDirs = targetAgents
            .Select(a => registry.GetConfig(a).SkillsDirectory)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();

        if (!hasMultipleSkillsDirs)
        {
            installMode = InstallMode.Copy;
        }
        else if (!options.Copy && !nonInteractive)
        {
            // WithDefault keeps the symlink default when the console cannot show the picker, matching the
            // non-interactive fallback noted below.
            installMode = await new SelectPrompt<InstallMode>("Installation method", s_installModeChoices)
                .WithDefault(InstallMode.Symlink)
                .ShowAsync(Output, cancellationToken);
        }
        // else: non-interactive, multiple distinct skills dirs → keep the symlink default.

        var overwriteTargets = GetOverwriteTargets(selectedSkills, installGlobally);

        if (!nonInteractive)
        {
            Output.Write(InstallPlanView.Create(
                selectedSkills,
                targetAgents,
                overwriteTargets.Select(o => (o.SkillName, o.DestinationPath)).ToArray()));

            // WithDefault proceeds (the install is the user's stated intent) when the console cannot show
            // the confirm, matching the skip-and-proceed taken on the non-interactive path.
            var confirmed = await new ConfirmPrompt("Proceed with installation?", defaultValue: true)
                .WithDefault(defaultValue: true)
                .ShowAsync(Output, cancellationToken);

            if (!confirmed)
            {
                Output.Warning("Installation cancelled");
                return ExitCodeConstants.Cancelled;
            }
        }

        var installOptions = new InstallOptions(installGlobally, WorkingDirectory: null, installMode);
        var existingSkills = overwriteTargets.Select(o => o.SkillName).ToImmutableHashSet(StringComparer.Ordinal);
        if (nonInteractive)
        {
            foreach (var overwrite in overwriteTargets)
            {
                Output.Warning(
                    $"Overwriting existing skill '{overwrite.SkillName}' at {overwrite.DestinationPath}");
            }
        }

        var results = await InstallSkillsAsync(selectedSkills, targetAgents, installOptions, cancellationToken);

        var successful = results.Where(r => r.Result.Success).ToImmutableArray();
        var failed = results.Where(r => !r.Result.Success).ToImmutableArray();

        var report = new InstallReport(
            parsed,
            targetAgents,
            successful,
            failed,
            existingSkills,
            installGlobally,
            installMode);

        if (report.Successful.Length > 0)
        {
            await recorder.RecordAsync(report, cancellationToken);
        }

        Output.Write(InstallationReportView.Create(report, registry, installer));

        return failed.Length > 0 ? ExitCodeConstants.Failure : ExitCodeConstants.Success;
    }

    private ImmutableArray<OverwriteTarget> GetOverwriteTargets(
        ImmutableArray<ResolvedSkill> selectedSkills,
        bool installGlobally)
    {
        var overwrites = ImmutableArray.CreateBuilder<OverwriteTarget>();
        foreach (var skill in selectedSkills)
        {
            var canonicalPath = installer.GetCanonicalPath(skill.InstallName, installGlobally);
            if (fileStore.PathExists(canonicalPath))
            {
                overwrites.Add(new OverwriteTarget(skill.InstallName, canonicalPath));
            }
        }

        return overwrites.ToImmutable();
    }

    private async Task<ImmutableArray<ResolvedSkill>> SelectSkillsAsync(
        ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> skillFilters,
        AddCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (skillFilters.Length > 0)
        {
            return skills.ApplyFilters(skillFilters);
        }

        if (skills.Length == 1 || options.Yes)
        {
            return skills;
        }

        if (consoleEnvironment.IsInputRedirected)
        {
            Output.Warning("Installation cancelled");
            return [];
        }

        return await skillSelector.SelectAsync(skills, cancellationToken);
    }

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
                // Sort the advisory list so the hint is deterministic and easy to scan; the
                // registry's own order is hash-bucket order and not meaningful to the user.
                var sortedValid = validAgents.OrderBy(a => a, StringComparer.Ordinal);
                throw new CliException(
                    ExitCodeConstants.Failure,
                    $"Invalid agents: {invalid.Join(", ")}",
                    title: "Invalid agents",
                    hint: $"Valid agents: {sortedValid.Join(", ")}");
            }

            return options.Agents;
        }

        // Select automatically (the installed agents plus universals) when we are non-interactive, when
        // exactly one agent is installed, or when the console cannot show the picker (output redirected
        // or a non-ANSI/TERM=dumb terminal). The picker is a branch, not a prompt, so WithDefault cannot
        // catch it - falling through to the selector here would pre-select last-used agents that may no
        // longer be installed, diverging from the -y path. Auto-select keeps the two paths in lockstep.
        var canPrompt = Output.Profile.Capabilities is { Interactive: true, Ansi: true };
        if (nonInteractive || !canPrompt || agentEnvironment.InstalledAgents.Length == 1)
        {
            return agentEnvironment
                .InstalledAgents.AddRange(registry.UniversalAgents)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();
        }

        // Zero or several installed in interactive mode: let the user choose. The pre-selection
        // (last-used, with a fallback) and persisting the choice live here, where the lock file does;
        // the selector stays pure.
        var defaults = await ResolveAgentDefaultsAsync(validAgents, cancellationToken);
        var selected = await agentSelector.SelectAsync(validAgents, defaults, cancellationToken);

        if (selected.Length > 0)
        {
            try
            {
                await globalLock.SaveLastSelectedAgentsAsync(selected, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Persisting the last-used selection is best-effort; a failure must not abort the install.
            }
        }

        return selected;
    }

    private async Task<ImmutableArray<string>> ResolveAgentDefaultsAsync(
        ImmutableArray<string> available,
        CancellationToken cancellationToken)
    {
        // Pre-selection: last-used (filtered to what's available) if any; otherwise all universals plus
        // whichever common defaults are present.
        try
        {
            var lastUsed = await globalLock.GetLastSelectedAgentsAsync(cancellationToken);
            if (lastUsed is { Length: > 0 } lastUsedAgents)
            {
                var availableSet = new HashSet<string>(available, StringComparer.Ordinal);
                var filtered = lastUsedAgents.Where(availableSet.Contains).ToImmutableArray();
                if (filtered.Length > 0)
                {
                    return filtered;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reading the last-used selection is best-effort; fall through to the computed defaults.
        }

        return available
            .Where(a => registry.IsUniversalAgent(a) || s_defaultSelection.Contains(a))
            .ToImmutableArray();
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
        await Output.StatusAsync("Installing skills...", async () =>
        {
            foreach (var skill in selectedSkills)
            {
                foreach (var agentType in targetAgents)
                {
                    InstallResult result;
                    try
                    {
                        result = await installer.InstallAsync(skill, agentType, installOptions, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A throwing installer must not unwind the whole command: record this
                        // single skill/agent pair as a failure and keep installing the rest.
                        result = new InstallResult(
                            Success: false,
                            Path: string.Empty,
                            Mode: installOptions.Mode,
                            Error: ex.Message);
                    }

                    results.Add(new InstallEntry(skill, agentType, result));
                }
            }
        });

        return results.ToImmutable();
    }
}

internal sealed record OverwriteTarget(string SkillName, string DestinationPath);

file static class Extensions
{
    internal static ImmutableArray<ResolvedSkill> ApplyFilters(
        this ImmutableArray<ResolvedSkill> skills,
        ImmutableArray<string> filters)
    {
        if (filters.Length == 0 || filters.Contains("*", StringComparer.Ordinal))
        {
            return skills;
        }

        return skills
            .Where(s => filters.Any(f => f.EqualsOrdinalIgnoreCase(s.InstallName) || f.EqualsOrdinalIgnoreCase(s.Name)))
            .ToImmutableArray();
    }

    internal static string? GetSkillFilter(this SkillSource source)
        => source is SkillSource.ISkillFilterable { SkillFilter: { Length: > 0 } filter } ? filter : null;

    internal static ImmutableArray<string> AppendFilter(this ImmutableArray<string> filters, string? newFilter)
    {
        if (string.IsNullOrEmpty(newFilter) || filters.Contains(newFilter, StringComparer.OrdinalIgnoreCase))
        {
            return filters;
        }

        return [.. filters, newFilter];
    }
}
