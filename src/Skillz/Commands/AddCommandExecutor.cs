using Microsoft.Extensions.DependencyInjection;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;

namespace Skillz.Commands;

internal sealed class AddCommandExecutor
{
    private readonly ISourceParser _sourceParser;
    private readonly IProviderRegistry _providerRegistry;
    private readonly IInstaller _installer;
    private readonly IInteractionService _interaction;
    private readonly IAgentRegistry _registry;
    private readonly IAgentEnvironmentDetector _detector;
    private readonly IProjectLockFile _projectLock;
    private readonly IGlobalLockFile _globalLock;
    private readonly IAddCommandPrompter _prompter;
    private readonly ConsoleEnvironment _consoleEnvironment;

    public AddCommandExecutor(IServiceProvider services)
    {
        _sourceParser = services.GetRequiredService<ISourceParser>();
        _providerRegistry = services.GetRequiredService<IProviderRegistry>();
        _installer = services.GetRequiredService<IInstaller>();
        _interaction = services.GetRequiredService<IInteractionService>();
        _registry = services.GetRequiredService<IAgentRegistry>();
        _detector = services.GetRequiredService<IAgentEnvironmentDetector>();
        _projectLock = services.GetRequiredService<IProjectLockFile>();
        _globalLock = services.GetRequiredService<IGlobalLockFile>();
        _prompter = services.GetRequiredService<IAddCommandPrompter>();
        _consoleEnvironment = services.GetRequiredService<ConsoleEnvironment>();
    }

    public async Task<CommandResult> RunAsync(AddCommandOptions options, CancellationToken cancellationToken)
    {
        ParsedSource parsed;
        try
        {
            parsed = _sourceParser.Parse(options.Source!);
        }
        catch (Exception ex)
        {
            _interaction.WriteError($"Invalid source: {ex.Message}");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        var skillFilters = MergeSkillFilters(options.SkillFilters, parsed);

        IReadOnlyList<RemoteSkill> skills;
        try
        {
            var provider = _providerRegistry.Resolve(parsed);
            skills = await _interaction.StatusAsync(
                "Fetching skills...",
                () => provider.FetchSkillsAsync(parsed, cancellationToken)
                    .ContinueWith(t => (IReadOnlyList<RemoteSkill>)t.Result, cancellationToken)).ConfigureAwait(false);
        }
        catch (CliException ex)
        {
            _interaction.WriteError(ex.Message);
            return new CommandResult.Failure(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _interaction.WriteError($"Failed to fetch skills: {ex.Message}");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        if (skills.Count == 0)
        {
            _interaction.WriteError("No valid skills found. Skills require a SKILL.md with name and description.");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        _interaction.WriteLine($"Found {skills.Count} skill(s)");

        if (options.List)
        {
            ListSkills(skills);
            return new CommandResult.Success();
        }

        var selectedSkills = await SelectSkillsAsync(skills, skillFilters, options, cancellationToken).ConfigureAwait(false);
        if (selectedSkills.Count == 0)
        {
            _interaction.WriteError("No matching skills.");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        var nonInteractive = await IsNonInteractiveAsync(options).ConfigureAwait(false);

        var targetAgents = await SelectAgentsAsync(options, nonInteractive, cancellationToken).ConfigureAwait(false);
        if (targetAgents is null)
        {
            return new CommandResult.Cancelled();
        }

        if (targetAgents.Count == 0)
        {
            _interaction.WriteError("No agents selected.");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        var installGlobally = options.Global;
        if (!options.Global && !nonInteractive)
        {
            var supportsGlobal = targetAgents.Any(a => _registry.GetConfig(a).GlobalSkillsDir is not null);
            if (supportsGlobal)
            {
                installGlobally = await _prompter.SelectGlobalScopeAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var installMode = options.Copy ? InstallMode.Copy : InstallMode.Symlink;
        var uniqueDirs = new HashSet<string>(targetAgents.Select(a => _registry.GetConfig(a).SkillsDir), StringComparer.Ordinal);
        if (uniqueDirs.Count <= 1)
        {
            installMode = InstallMode.Copy;
        }
        else if (!options.Copy && !nonInteractive)
        {
            installMode = await _prompter.SelectInstallModeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!nonInteractive)
        {
            var confirmed = await _prompter.ConfirmInstallationAsync(selectedSkills, targetAgents, cancellationToken)
                .ConfigureAwait(false);
            if (!confirmed)
            {
                _interaction.WriteWarning("Installation cancelled");
                return new CommandResult.Cancelled();
            }
        }

        var installOptions = new InstallOptions(installGlobally, Cwd: null, installMode);
        var results = await InstallSkillsAsync(selectedSkills, targetAgents, installOptions, cancellationToken)
            .ConfigureAwait(false);

        var successful = results.Where(r => r.Result.Success).ToList();
        var failed = results.Where(r => !r.Result.Success).ToList();

        if (successful.Count > 0)
        {
            await UpdateLocksAsync(parsed, successful, installGlobally, cancellationToken).ConfigureAwait(false);
            _interaction.WriteSuccess($"Installed {successful.Select(r => r.SkillName).Distinct().Count()} skill(s)");
        }

        if (failed.Count > 0)
        {
            _interaction.WriteError($"Failed to install {failed.Count} entry/entries");
            foreach (var r in failed)
            {
                _interaction.WriteError($"  {r.SkillName} -> {r.AgentType}: {r.Result.Error}");
            }

            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        return new CommandResult.Success();
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
        _interaction.WriteLine();
        _interaction.WriteMarkupLine("[bold]Available Skills[/]");
        foreach (var skill in skills)
        {
            _interaction.WriteMarkupLine($"  [cyan]{skill.InstallName}[/]");
            _interaction.WriteDim($"    {skill.Description}");
        }

        _interaction.WriteLine();
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

            return skills.Where(s => skillFilters.Any(f =>
                string.Equals(f, s.InstallName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, s.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (skills.Count == 1 || options.Yes)
        {
            return skills;
        }

        return await _prompter.SelectSkillsAsync(skills, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsNonInteractiveAsync(AddCommandOptions options)
    {
        if (options.Yes || options.All)
        {
            return true;
        }

        if (_consoleEnvironment.IsInputRedirected)
        {
            return true;
        }

        var detection = await _detector.DetectAgentAsync().ConfigureAwait(false);
        return detection.IsAgent;
    }

    private async Task<IReadOnlyList<string>?> SelectAgentsAsync(
        AddCommandOptions options,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        var validAgents = _registry.ListAgentTypes();

        if (options.Agents.Count > 0)
        {
            if (options.Agents.Contains("*", StringComparer.Ordinal))
            {
                return validAgents;
            }

            var invalid = options.Agents.Where(a => !validAgents.Contains(a)).ToList();
            if (invalid.Count > 0)
            {
                _interaction.WriteError($"Invalid agents: {string.Join(", ", invalid)}");
                return Array.Empty<string>();
            }

            return options.Agents;
        }

        var installed = await _detector.DetectInstalledAgentsAsync().ConfigureAwait(false);

        if (installed.Count > 0 && nonInteractive)
        {
            return EnsureUniversalAgents(installed);
        }

        if (nonInteractive)
        {
            return validAgents;
        }

        var detection = await _detector.DetectAgentAsync().ConfigureAwait(false);
        if (detection.IsAgent && detection.Name is { } agentName)
        {
            var mapped = _detector.GetAgentType(agentName);
            if (mapped is not null)
            {
                return EnsureUniversalAgents([mapped]);
            }
        }

        return await _prompter.SelectAgentsAsync(validAgents, options.Global, cancellationToken)
            .ConfigureAwait(false);
    }

    private IReadOnlyList<string> EnsureUniversalAgents(IReadOnlyList<string> agents)
    {
        var universal = _registry.GetUniversalAgents();
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

    private async Task<List<InstallEntry>> InstallSkillsAsync(
        IReadOnlyList<RemoteSkill> selectedSkills,
        IReadOnlyList<string> targetAgents,
        InstallOptions installOptions,
        CancellationToken cancellationToken)
    {
        var results = new List<InstallEntry>();
        await _interaction.StatusAsync("Installing skills...", async () =>
        {
            foreach (var skill in selectedSkills)
            {
                foreach (var agentType in targetAgents)
                {
                    var result = await _installer.InstallRemoteSkillForAgentAsync(skill, agentType, installOptions, cancellationToken)
                        .ConfigureAwait(false);
                    results.Add(new InstallEntry(skill, agentType, result));
                }
            }
        }).ConfigureAwait(false);

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
                        skillFolderHash = await _projectLock.ComputeSkillFolderHashAsync(installPath, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch
                {
                }

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
                try
                {
                    await _globalLock.AddEntryAsync(entry.Skill.InstallName, lockEntry, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
            else
            {
                var computedHash = string.Empty;
                try
                {
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        computedHash = await _projectLock.ComputeSkillFolderHashAsync(installPath, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch
                {
                }

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
                    await _projectLock.AddEntryAsync(entry.Skill.InstallName, lockEntry, cwd: null, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }

    private static string GetSourceUrl(ParsedSource source) => source switch
    {
        ParsedSource.GitHub gh => gh.Url,
        ParsedSource.GitLab gl => gl.Url,
        ParsedSource.Git g => g.Url,
        ParsedSource.Local l => l.Url,
        ParsedSource.WellKnown w => w.Url,
        _ => string.Empty
    };

    private static string GetSourceType(ParsedSource source) => source switch
    {
        ParsedSource.GitHub => "github",
        ParsedSource.GitLab => "gitlab",
        ParsedSource.Git => "git",
        ParsedSource.Local => "local",
        ParsedSource.WellKnown => "well-known",
        _ => "unknown"
    };

    private static string? GetSourceRef(ParsedSource source) => source switch
    {
        ParsedSource.GitHub gh => gh.Ref,
        ParsedSource.GitLab gl => gl.Ref,
        ParsedSource.Git g => g.Ref,
        _ => null
    };

    private sealed record InstallEntry(RemoteSkill Skill, string AgentType, InstallResult Result)
    {
        public string SkillName => Skill.InstallName;
    }
}
