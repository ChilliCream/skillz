using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Plugins;
using Skillz.Skills;

namespace Skillz.Commands;

internal sealed class RemoveCommand : BaseCommand
{
    private readonly IServiceProvider _services;
    private readonly Argument<string[]> _skillsArgument;
    private readonly Option<bool> _globalOption;
    private readonly Option<string[]> _agentOption;
    private readonly Option<bool> _yesOption;
    private readonly Option<bool> _allOption;

    public RemoveCommand(IServiceProvider services)
        : base("remove", "Remove installed skills")
    {
        _services = services;

        _skillsArgument = new Argument<string[]>("skills")
        {
            Description = "Skill names to remove",
            Arity = ArgumentArity.ZeroOrMore
        };
        Arguments.Add(_skillsArgument);

        _globalOption = new Option<bool>(CommonOptionNames.Global, "-g")
        {
            Description = "Remove from global installation"
        };
        Options.Add(_globalOption);

        _agentOption = new Option<string[]>(CommonOptionNames.Agent, "-a")
        {
            Description = "Target agent(s)",
            AllowMultipleArgumentsPerToken = true
        };
        Options.Add(_agentOption);

        _yesOption = new Option<bool>(CommonOptionNames.Yes, "-y")
        {
            Description = "Skip prompts (non-interactive)"
        };
        Options.Add(_yesOption);

        _allOption = new Option<bool>(CommonOptionNames.All)
        {
            Description = "Remove all installed skills"
        };
        Options.Add(_allOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var requestedSkills = parseResult.GetValue(_skillsArgument) ?? Array.Empty<string>();
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? Array.Empty<string>();
        var yes = parseResult.GetValue(_yesOption);
        var all = parseResult.GetValue(_allOption);

        var installer = _services.GetRequiredService<IInstaller>();
        var registry = _services.GetRequiredService<IAgentRegistry>();
        var interaction = _services.GetRequiredService<IInteractionService>();
        var prompter = _services.GetRequiredService<IRemoveCommandPrompter>();
        var projectLock = _services.GetRequiredService<IProjectLockFile>();
        var globalLock = _services.GetRequiredService<IGlobalLockFile>();
        var detector = _services.GetRequiredService<IAgentEnvironmentDetector>();
        var consoleEnvironment = _services.GetRequiredService<ConsoleEnvironment>();

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
        var installed = await ScanInstalledSkillsAsync(installer, registry, global, cwd).ConfigureAwait(false);

        if (installed.Count == 0)
        {
            interaction.WriteWarning("No skills found to remove.");
            return new CommandResult.Success();
        }

        var nonInteractive = yes || all || consoleEnvironment.IsInputRedirected
            || (await detector.DetectAgentAsync().ConfigureAwait(false)).IsAgent;

        IReadOnlyList<string> selected;
        if (all)
        {
            selected = installed;
        }
        else if (requestedSkills.Length > 0)
        {
            selected = installed.Where(s => requestedSkills.Any(r =>
                string.Equals(r, s, StringComparison.OrdinalIgnoreCase))).ToList();

            if (selected.Count == 0)
            {
                interaction.WriteError($"No matching skills found for: {string.Join(", ", requestedSkills)}");
                return new CommandResult.Failure(ExitCodeConstants.Failure);
            }
        }
        else if (nonInteractive)
        {
            interaction.WriteError("No skills specified for removal.");
            return new CommandResult.Failure(ExitCodeConstants.Failure);
        }
        else
        {
            selected = await prompter.SelectSkillsAsync(installed, cancellationToken).ConfigureAwait(false);
            if (selected.Count == 0)
            {
                interaction.WriteWarning("Removal cancelled");
                return new CommandResult.Cancelled();
            }
        }

        var targetAgents = agents.Length > 0
            ? (IReadOnlyList<string>)agents
            : registry.ListAgentTypes();

        if (!nonInteractive)
        {
            var confirmed = await prompter.ConfirmRemovalAsync(selected, cancellationToken).ConfigureAwait(false);
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
                        await globalLock.RemoveEntryAsync(skillName, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await projectLock.RemoveEntryAsync(skillName, cwd, cancellationToken).ConfigureAwait(false);
                    }

                    removed++;
                }
                catch (Exception ex)
                {
                    failures.Add((skillName, ex.Message));
                }
            }
        }).ConfigureAwait(false);

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

    private static async Task<IReadOnlyList<string>> ScanInstalledSkillsAsync(
        IInstaller installer,
        IAgentRegistry registry,
        bool global,
        string cwd)
    {
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

        await Task.CompletedTask.ConfigureAwait(false);
        return skills.OrderBy(s => s, StringComparer.Ordinal).ToList();
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
