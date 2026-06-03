using System.Collections.Immutable;
using System.CommandLine;
using System.Text.Json;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Utils;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class ListCommand(
    ISkillInstaller installer,
    AgentRegistry registry,
    IInteractionService interaction,
    IFileStore fileStore,
    ISystemEnvironment systemEnvironment,
    CliExecutionContext executionContext) : BaseCommand("list", "List installed skills")
{
    private readonly Option<bool> _globalOption = new(CommonOptionNames.Global, "-g")
    {
        Description = "List global skills"
    };

    private readonly Option<string[]> _agentOption = new(CommonOptionNames.Agent, "-a")
    {
        Description = "Filter by agent",
        AllowMultipleArgumentsPerToken = true
    };

    private readonly Option<string?> _formatOption = new(CommonOptionNames.FormatJson)
    {
        Description = "Output format (text|json)"
    };

    private readonly Option<bool> _jsonOption = new("--json")
    {
        Description = "Output as JSON (alias for --format json)"
    };

    protected override void Configure()
    {
        Options.Add(_globalOption);
        Options.Add(_agentOption);
        Options.Add(_formatOption);
        Options.Add(_jsonOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? [];
        var format = parseResult.GetValue(_formatOption);
        var jsonFlag = parseResult.GetValue(_jsonOption);
        var jsonOutput = jsonFlag || format.EqualsOrdinalIgnoreCase("json");

        executionContext.IsJsonOutput = jsonOutput;

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

        var skills = CollectInstalledSkills(installer, registry, agents, global, cancellationToken);

        if (jsonOutput)
        {
            var payload = skills.Select(s => s.ToJsonType(global, registry)).ToArray();

            var json = JsonSerializer.Serialize(payload, JsonSourceGenerationContext.Default.InstalledSkillJsonArray);
            Console.WriteLine(json);
            return new CommandResult.Success();
        }

        if (skills.Length == 0)
        {
            interaction.WriteDim(global ? "No global skills found." : "No project skills found.");
            if (!global)
            {
                interaction.WriteDim("Try listing global skills with -g");
            }
            return new CommandResult.Success();
        }

        var scopeLabel = global ? "Global" : "Project";
        interaction.WriteMarkupLine($"[bold]{scopeLabel} Skills[/]");
        interaction.WriteLine();

        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn().PadRight(0));
        grid.AddRow("[grey66]Skill[/]", "[grey66]Path[/]", "[grey66]Agents[/]");

        foreach (var skill in skills)
        {
            var agentNames = skill.Agents.GetDisplayNames(registry).ToList();

            string agentCell;
            if (agentNames.Count == 0)
            {
                agentCell = "[yellow]not linked[/]";
            }
            else
            {
                var display =
                    agentNames.Count > 5
                        ? agentNames.Take(5).Join(", ") + $" +{agentNames.Count - 5} more"
                        : agentNames.Join(", ");
                agentCell = $"[dim]{Markup.Escape(display)}[/]";
            }

            grid.AddRow(
                $"[cyan]{Markup.Escape(skill.Name)}[/]",
                $"[dim]{Markup.Escape(ShortenPath(skill.CanonicalPath))}[/]",
                agentCell);
        }

        interaction.WriteRenderable(grid);

        return new CommandResult.Success();
    }

    private ImmutableArray<InstalledSkill> CollectInstalledSkills(
        ISkillInstaller installer,
        AgentRegistry registry,
        string[] agents,
        bool global,
        CancellationToken cancellationToken)
    {
        var cwd = systemEnvironment.CurrentDirectory;
        var agentFilter = agents.Length > 0 ? [.. agents] : registry.AgentTypes;
        var skills = new Dictionary<string, InstalledSkill>(StringComparer.Ordinal);
        var canonicalDir = installer.GetCanonicalSkillsDirectory(global, cwd);
        var canonicalDirFull = Path.GetFullPath(canonicalDir);

        if (fileStore.DirectoryExists(canonicalDir))
        {
            foreach (var entry in fileStore.EnumerateDirectories(canonicalDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var skillMd = Path.Combine(entry, KnownConfigNames.SkillFileName);
                if (!fileStore.FileExists(skillMd))
                {
                    continue;
                }

                skills[name] = new InstalledSkill(name, entry, []);
            }
        }

        foreach (var agentType in agentFilter)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!registry.TryGetConfig(agentType, out var config) || config is null)
            {
                continue;
            }

            if (global && config.GlobalSkillsDirectory is null)
            {
                continue;
            }

            var agentDir = installer.GetAgentBaseDirectory(agentType, global, cwd);
            var agentDirFull = Path.GetFullPath(agentDir);

            // Skip if this agent's dir IS the canonical dir (universal agents)
            if (string.Equals(agentDirFull, canonicalDirFull, PathContainment.Comparison))
            {
                continue;
            }

            if (!fileStore.DirectoryExists(agentDir))
            {
                continue;
            }

            foreach (var entry in fileStore.EnumerateDirectories(agentDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var skillMd = Path.Combine(entry, KnownConfigNames.SkillFileName);
                if (!fileStore.FileExists(skillMd))
                {
                    continue;
                }

                if (!skills.TryGetValue(name, out var existing))
                {
                    existing = new InstalledSkill(name, entry, []);
                    skills[name] = existing;
                }

                if (!existing.Agents.Contains(agentType, StringComparer.Ordinal))
                {
                    existing.Agents.Add(agentType);
                }
            }
        }

        return [.. skills.Values];
    }

    private string ShortenPath(string path)
    {
        var home = systemEnvironment.HomeDirectory;
        if (!string.IsNullOrEmpty(home) && path.StartsWithOrdinal(home))
        {
            return "~" + path[home.Length..];
        }
        var cwd = systemEnvironment.CurrentDirectory;
        if (!string.IsNullOrEmpty(cwd) && path.StartsWithOrdinal(cwd))
        {
            var relative = path[cwd.Length..];
            return "." + (relative.Length == 0 ? "" : relative);
        }
        return path;
    }

    internal sealed record InstalledSkill(string Name, string CanonicalPath, List<string> Agents);
}

internal sealed record InstalledSkillJson(string Name, string Path, string Scope, string[] Agents);

file static class Extensions
{
    public static InstalledSkillJson ToJsonType(
        this ListCommand.InstalledSkill skill,
        bool global,
        AgentRegistry registry)
    {
        return new InstalledSkillJson(
            skill.Name,
            skill.CanonicalPath,
            global ? "global" : "project",
            skill.Agents.GetDisplayNames(registry).ToArray());
    }

    public static IEnumerable<string> GetDisplayNames(this IEnumerable<string> agents, AgentRegistry registry)
        => agents.Select(a => registry.TryGetConfig(a, out var c) && c is not null ? c.DisplayName : a);
}
