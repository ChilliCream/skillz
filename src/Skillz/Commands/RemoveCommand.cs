using System.Collections.Immutable;
using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Skills;

namespace Skillz.Commands;

internal sealed class RemoveCommand(
    IInstaller installer,
    IAgentRegistry registry,
    IInteractionService interaction,
    IRemoveCommandPrompter prompter,
    IProjectLockFile projectLock,
    IGlobalLockFile globalLock,
    IAgentEnvironmentDetector detector,
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
            var valid = registry.ListAgentTypes();
            var invalid = agents.Where(a => !valid.Contains(a)).ToList();
            if (invalid.Count > 0)
            {
                interaction.WriteError($"Invalid agents: {string.Join(", ", invalid)}");
                return new CommandResult.Failure(ExitCodeConstants.Failure);
            }
        }

        var cwd = Directory.GetCurrentDirectory();
        var installed = await ScanInstalledSkillsAsync(installer, registry, global, cwd, cancellationToken);

        if (installed.Length == 0)
        {
            interaction.WriteWarning("No skills found to remove.");
            return new CommandResult.Success();
        }

        var nonInteractive =
            yes
            || all
            || consoleEnvironment.IsInputRedirected
            || (await detector.DetectAgentAsync(cancellationToken)).IsAgent;

        ImmutableArray<string> selected;
        if (all)
        {
            selected = installed;
        }
        else if (requestedSkills.Length > 0)
        {
            selected = installed
                .Where(s => requestedSkills.Any(r => string.Equals(r, s, StringComparison.OrdinalIgnoreCase)))
                .ToImmutableArray();

            if (selected.Length == 0)
            {
                interaction.WriteDim($"No matching skills found for: {string.Join(", ", requestedSkills)}");
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

        var targetAgents = agents.Length > 0 ? (IReadOnlyList<string>)agents : registry.ListAgentTypes();

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

        await interaction
            .StatusAsync("Removing skills...", async () => { foreach (var skillName in selected)
                {
                    try
                    {
                        var canonicalPath = installer.GetCanonicalPath(skillName, global, cwd);

                        foreach (var agentType in targetAgents)
                        {
                            var installPath = installer.GetInstallPath(skillName, agentType, global, cwd);
                            if (string.Equals(installPath, canonicalPath, GetPathComparison()))
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
                    catch (Exception ex)
                    {
                        failures.Add((skillName, ex.Message));
                    }
                } });

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

    private static async Task<ImmutableArray<string>> ScanInstalledSkillsAsync(
        IInstaller installer,
        IAgentRegistry registry,
        bool global,
        string cwd,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var skills = new HashSet<string>(StringComparer.Ordinal);
        var directoriesToScan = new HashSet<string>(GetPathComparer());

        directoriesToScan.Add(installer.GetCanonicalSkillsDir(global, cwd));

        foreach (var agentType in registry.ListAgentTypes())
        {
            var config = registry.GetConfig(agentType);
            if (global && config.GlobalSkillsDir is null)
            {
                continue;
            }

            directoriesToScan.Add(installer.GetAgentBaseDir(agentType, global, cwd));
        }

        foreach (var dir in directoriesToScan)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateDirectories(dir))
            {
                skills.Add(Path.GetFileName(entry));
            }
        }

        await Task.CompletedTask;
        return [.. skills.OrderBy(s => s, StringComparer.Ordinal)];
    }

    private static bool IsCanonicalStillUsed(
        IInstaller installer,
        IAgentRegistry registry,
        string skillName,
        bool global,
        string cwd,
        IReadOnlyList<string> removedAgents)
    {
        var removedSet = new HashSet<string>(removedAgents, StringComparer.Ordinal);
        foreach (var agentType in registry.ListAgentTypes())
        {
            if (removedSet.Contains(agentType))
            {
                continue;
            }

            var path = installer.GetInstallPath(skillName, agentType, global, cwd);
            if (PathExists(path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    info.Delete();
                }
                else
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort
        }
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }
}
