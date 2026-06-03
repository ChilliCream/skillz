using System.Collections.Immutable;
using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Utils;

namespace Skillz.Commands;

internal sealed class RemoveCommand(
    ISkillInstaller installer,
    AgentRegistry registry,
    IInteractionService interaction,
    IRemoveCommandPrompter prompter,
    IProjectLockFile projectLock,
    IGlobalLockFile globalLock,
    AgentEnvironment agentEnvironment,
    IFileStore fileStore,
    ISystemEnvironment systemEnvironment,
    ConsoleEnvironment consoleEnvironment) : BaseCommand("remove", "Remove installed skills")
{
    private readonly Argument<string[]> _skillsArgument = new("skills")
    {
        Description = "Skill names to remove",
        Arity = ArgumentArity.ZeroOrMore
    };

    private readonly Option<bool> _globalOption = new(CommonOptionNames.Global, "-g")
    {
        Description = "Remove from global installation"
    };

    private readonly Option<string[]> _agentOption = new(CommonOptionNames.Agent, "-a")
    {
        Description = "Target agent(s)",
        AllowMultipleArgumentsPerToken = true
    };

    private readonly Option<bool> _yesOption = new(CommonOptionNames.Yes, "-y")
    {
        Description = "Skip prompts (non-interactive)"
    };

    private readonly Option<bool> _allOption = new(CommonOptionNames.All)
    {
        Description = "Remove all installed skills"
    };

    protected override void Configure()
    {
        Arguments.Add(_skillsArgument);
        Options.Add(_globalOption);
        Options.Add(_agentOption);
        Options.Add(_yesOption);
        Options.Add(_allOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        var requestedSkills = parseResult.GetValue(_skillsArgument) ?? [];
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? [];
        var yes = parseResult.GetValue(_yesOption);
        var all = parseResult.GetValue(_allOption);

        if (agents.Length > 0)
        {
            var valid = registry.AgentTypes;
            var invalid = agents.Where(a => !valid.Contains(a)).ToList();
            if (invalid.Count > 0)
            {
                interaction.WriteError($"Invalid agents: {invalid.Join(", ")}");
                return new CommandResult.Failure(ExitCodeConstants.Failure);
            }
        }

        var cwd = systemEnvironment.CurrentDirectory;
        var installed = CollectInstalledSkills(installer, registry, global);

        if (installed.Length == 0)
        {
            interaction.WriteWarning("No skills found to remove.");
            return new CommandResult.Success();
        }

        var nonInteractive =
            yes || all || consoleEnvironment.IsInputRedirected || agentEnvironment.IsRunningInsideAgent;

        var selected = ImmutableArray<string>.Empty;
        if (all)
        {
            selected = installed;
        }
        else if (requestedSkills.Length > 0)
        {
            selected = installed.Where(s => requestedSkills.Any(r => r.EqualsOrdinalIgnoreCase(s))).ToImmutableArray();

            if (selected.Length == 0)
            {
                interaction.WriteDim($"No matching skills found for: {requestedSkills.Join(", ")}");
                return new CommandResult.Success();
            }
        }
        else if (nonInteractive)
        {
            interaction.WriteDim("No skills specified for removal.");
            return new CommandResult.Success();
        }
        else
        {
            selected = await prompter.SelectSkillsAsync(installed, cancellationToken);
            if (selected.Length == 0)
            {
                interaction.WriteWarning("Removal cancelled");
                return new CommandResult.Cancelled();
            }
        }

        var targetAgents = agents.Length > 0 ? agents.ToImmutableArray() : registry.AgentTypes;

        if (!nonInteractive)
        {
            var confirmed = await prompter.ConfirmRemovalAsync(selected, cancellationToken);
            if (!confirmed)
            {
                interaction.WriteWarning("Removal cancelled");
                return new CommandResult.Cancelled();
            }
        }

        var failures = new List<(string Skill, string Error)>();
        var removed = 0;

        await interaction.StatusAsync("Removing skills...", async () =>
        {
            foreach (var skillName in selected)
            {
                try
                {
                    var canonicalPath = installer.GetCanonicalPath(skillName, global, cwd);

                    foreach (var agentType in targetAgents)
                    {
                        var installPath = installer.GetInstallPath(skillName, agentType, global, cwd);
                        if (string.Equals(installPath, canonicalPath, PathContainment.Comparison))
                        {
                            continue;
                        }

                        TryDeletePath(installPath);
                    }

                    if (!IsCanonicalStillUsed(installer, registry, skillName, global, cwd, targetAgents))
                    {
                        TryDeletePath(canonicalPath);
                    }

                    if (global)
                    {
                        await globalLock.RemoveEntryAsync(skillName, cancellationToken);
                    }
                    else
                    {
                        await projectLock.RemoveEntryAsync(skillName, cwd, cancellationToken);
                    }

                    removed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add((skillName, ex.Message));
                }
            }
        });

        if (removed > 0)
        {
            interaction.WriteSuccess($"Successfully removed {removed} skill(s)");
        }

        if (failures.Count > 0)
        {
            interaction.WriteError($"Failed to remove {failures.Count} skill(s)");
            foreach (var (skill, error) in failures)
            {
                interaction.WriteError($"  {skill}: {error}");
            }

            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }

        return new CommandResult.Success();
    }

    private ImmutableArray<string> CollectInstalledSkills(
        ISkillInstaller installer,
        AgentRegistry registry,
        bool global)
    {
        var cwd = systemEnvironment.CurrentDirectory;
        var skills = new HashSet<string>(StringComparer.Ordinal);
        var directoriesToScan = new HashSet<string>(PathContainment.Comparer)
        {
            installer.GetCanonicalSkillsDirectory(global, cwd)
        };

        foreach (var agentType in registry.AgentTypes)
        {
            var config = registry.GetConfig(agentType);
            if (global && config.GlobalSkillsDirectory is null)
            {
                continue;
            }

            directoriesToScan.Add(installer.GetAgentBaseDirectory(agentType, global, cwd));
        }

        foreach (var dir in directoriesToScan)
        {
            if (!fileStore.DirectoryExists(dir))
            {
                continue;
            }

            foreach (var entry in fileStore.EnumerateDirectories(dir))
            {
                skills.Add(Path.GetFileName(entry));
            }
        }

        return [.. skills.OrderBy(s => s, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Returns <see langword="true"/> if at least one agent outside <paramref name="removedAgents"/>
    /// still has an install path for the skill, meaning the canonical directory must be kept.
    /// </summary>
    private bool IsCanonicalStillUsed(
        ISkillInstaller installer,
        AgentRegistry registry,
        string skillName,
        bool global,
        string cwd,
        IReadOnlyList<string> removedAgents)
    {
        var removedSet = new HashSet<string>(removedAgents, StringComparer.Ordinal);
        foreach (var agentType in registry.AgentTypes)
        {
            if (removedSet.Contains(agentType))
            {
                continue;
            }

            var path = installer.GetInstallPath(skillName, agentType, global, cwd);
            if (fileStore.PathExists(path))
            {
                return true;
            }
        }

        return false;
    }

    private void TryDeletePath(string path)
    {
        try
        {
            fileStore.DeletePath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort
        }
    }
}
