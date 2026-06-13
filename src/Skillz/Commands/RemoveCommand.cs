using System.Collections.Immutable;
using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Skillz.Interaction.Prompts;
using Skillz.Locking;
using Skillz.Paths;
using Skillz.Skills;
using Skillz.Utils;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class RemoveCommand(
    ISkillInstaller installer,
    AgentRegistry registry,
    IAnsiConsole console,
    IProjectLockFile projectLock,
    IGlobalLockFile globalLock,
    AgentEnvironment agentEnvironment,
    IFileStore fileStore,
    ISystemEnvironment systemEnvironment,
    ConsoleEnvironment consoleEnvironment) : BaseCommand(console, "remove", "Remove installed skills")
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

    protected override async Task<int> ExecuteAsync(
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
                Output.Error($"Invalid agents: {invalid.Join(", ")}");
                return ExitCodeConstants.Failure;
            }
        }

        var cwd = systemEnvironment.CurrentDirectory;
        var installed = CollectInstalledSkills(installer, registry, global);

        if (installed.Length == 0)
        {
            Output.Warning("No skills found to remove.");
            return ExitCodeConstants.Success;
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
                Output.Dim($"No matching skills found for: {requestedSkills.Join(", ")}");
                return ExitCodeConstants.Success;
            }
        }
        else if (nonInteractive)
        {
            Output.Dim("No skills specified for removal.");
            return ExitCodeConstants.Success;
        }
        else
        {
            var choices = installed.Select(s => (s, s));

            // WithDefault degrades to an empty selection when the console cannot drive the key loop
            // (e.g. output is redirected while stdin is still a TTY), so the run cancels gracefully
            // below instead of throwing from Spectre.
            selected = await new MultiSelectPrompt<string>("Select skills to remove", choices)
                .RequireNonEmpty()
                .WithDefault(ImmutableArray<string>.Empty)
                .ShowAsync(new VimNavConsole(Output), cancellationToken);
            if (selected.Length == 0)
            {
                Output.Warning("Removal cancelled");
                return ExitCodeConstants.Cancelled;
            }
        }

        var targetAgents = agents.Length > 0 ? agents.ToImmutableArray() : registry.AgentTypes;

        if (!nonInteractive)
        {
            var message = $"Are you sure you want to remove {selected.Length} skill(s) [{selected.Join(", ")}]?";
            // Removal is destructive, so when the console cannot show the confirm (redirected or
            // non-ANSI) WithDefault DECLINES rather than proceeds - the user must pass -y to remove
            // non-interactively. This matches the prompt's own decline-by-default.
            var confirmed = await new ConfirmPrompt(message, defaultValue: false)
                .WithDefault(defaultValue: false)
                .ShowAsync(Output, cancellationToken);
            if (!confirmed)
            {
                Output.Warning("Removal cancelled");
                return ExitCodeConstants.Cancelled;
            }
        }

        var failures = new List<(string Skill, string Error)>();
        var removed = 0;

        await Output.StatusAsync("Removing skills...", async () =>
        {
            foreach (var skillName in selected)
            {
                try
                {
                    var canonicalPath = installer.GetCanonicalPath(skillName, global, cwd);

                    var deletedAnything = false;
                    foreach (var agentType in targetAgents)
                    {
                        var installPath = installer.GetInstallPath(skillName, agentType, global, cwd);
                        if (string.Equals(installPath, canonicalPath, SafePath.Comparison))
                        {
                            continue;
                        }

                        deletedAnything |= TryDeletePath(installPath);
                    }

                    if (!IsCanonicalStillUsed(installer, registry, skillName, global, cwd, targetAgents))
                    {
                        deletedAnything |= TryDeletePath(canonicalPath);
                    }

                    var lockEntryRemoved = global
                        ? await globalLock.RemoveEntryAsync(skillName, cancellationToken)
                        : await projectLock.RemoveEntryAsync(skillName, cwd, cancellationToken);

                    // The discovered name may be an unsanitized on-disk folder that does not map to
                    // the sanitized install/canonical paths, so nothing on disk or in the lock was
                    // actually removed. Report that accurately instead of claiming success.
                    if (deletedAnything || lockEntryRemoved)
                    {
                        removed++;
                    }
                    else
                    {
                        failures.Add((skillName, "Nothing was removed (no matching files or lock entry found)."));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add((skillName, ex.Message));
                }
            }
        });

        if (removed > 0)
        {
            Output.Success($"Successfully removed {removed} skill(s)");
        }

        if (failures.Count > 0)
        {
            Output.Error($"Failed to remove {failures.Count} skill(s)");
            foreach (var (skill, error) in failures)
            {
                Output.Error($"  {skill}: {error}");
            }

            return ExitCodeConstants.Failure;
        }

        return ExitCodeConstants.Success;
    }

    private ImmutableArray<string> CollectInstalledSkills(
        ISkillInstaller installer,
        AgentRegistry registry,
        bool global)
    {
        var cwd = systemEnvironment.CurrentDirectory;
        var skills = new HashSet<string>(StringComparer.Ordinal);
        var directoriesToScan = new HashSet<string>(SafePath.Comparer)
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

    /// <summary>
    /// Deletes the path when it exists and reports whether something was actually removed.
    /// Returns <see langword="false"/> when nothing existed at <paramref name="path"/> or the
    /// best-effort deletion failed, so callers can report removal accurately.
    /// </summary>
    private bool TryDeletePath(string path)
    {
        if (!fileStore.PathExists(path))
        {
            return false;
        }

        try
        {
            fileStore.DeletePath(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort
            return false;
        }
    }
}
