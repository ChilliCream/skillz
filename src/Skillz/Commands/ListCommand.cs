using System.Collections.Immutable;
using System.CommandLine;
using System.Text.Json;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Skills;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class ListCommand(
    IInstaller installer,
    IAgentRegistry registry,
    IInteractionService interaction,
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

    protected override async Task<CommandResult> ExecuteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? [];
        var format = parseResult.GetValue(_formatOption);
        var jsonFlag = parseResult.GetValue(_jsonOption);
        var jsonOutput = jsonFlag || string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

        executionContext.IsJsonOutput = jsonOutput;

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
        ImmutableArray<string> agentFilter = agents.Length > 0 ? [.. agents] : registry.ListAgentTypes();

        var skills = await CollectInstalledSkillsAsync(installer, registry, agentFilter, global, cwd, cancellationToken);

        if (jsonOutput)
        {
            var payload = skills
                .Select(s => new InstalledSkillJson(
                    s.Name,
                    s.CanonicalPath,
                    global ? "global" : "project",
                    s.Agents.Select(a => registry.TryGetConfig(a, out var c) && c is not null ? c.DisplayName : a)
                        .ToArray()))
                .ToArray();

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

        foreach (var skill in skills)
        {
            var agentNames = skill
                .Agents.Select(a => registry.TryGetConfig(a, out var c) && c is not null ? c.DisplayName : a)
                .ToList();
            string agentDisplay;
            if (agentNames.Count == 0)
            {
                agentDisplay = "not linked";
            }
            else if (agentNames.Count > 5)
            {
                agentDisplay = string.Join(", ", agentNames.Take(5)) + $" +{agentNames.Count - 5} more";
            }
            else
            {
                agentDisplay = string.Join(", ", agentNames);
            }

            interaction.WriteMarkupLine(
                $"[cyan]{Markup.Escape(skill.Name)}[/] [dim]{Markup.Escape(ShortenPath(skill.CanonicalPath))}[/]");
            if (agentNames.Count == 0)
            {
                interaction.WriteMarkupLine("  Agents: [yellow]not linked[/]");
            }
            else
            {
                interaction.WriteDim($"  Agents: {agentDisplay}");
            }
        }

        return new CommandResult.Success();
    }

    private static async Task<ImmutableArray<InstalledSkill>> CollectInstalledSkillsAsync(
        IInstaller installer,
        IAgentRegistry registry,
        ImmutableArray<string> agentFilter,
        bool global,
        string cwd,
        CancellationToken cancellationToken)
    {
        var skills = new Dictionary<string, InstalledSkill>(StringComparer.Ordinal);
        var canonicalDir = installer.GetCanonicalSkillsDir(global, cwd);
        var canonicalDirFull = Path.GetFullPath(canonicalDir);

        if (Directory.Exists(canonicalDir))
        {
            foreach (var entry in Directory.EnumerateDirectories(canonicalDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var skillMd = Path.Combine(entry, KnownConfigNames.SkillFileName);
                if (!File.Exists(skillMd))
                {
                    continue;
                }

                skills[name] = new InstalledSkill(name, entry, []);
            }
        }

        var pathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var agentType in agentFilter)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!registry.TryGetConfig(agentType, out var config) || config is null)
            {
                continue;
            }

            if (global && config.GlobalSkillsDir is null)
            {
                continue;
            }

            var agentDir = installer.GetAgentBaseDir(agentType, global, cwd);
            var agentDirFull = Path.GetFullPath(agentDir);

            // Skip if this agent's dir IS the canonical dir (universal agents)
            if (string.Equals(agentDirFull, canonicalDirFull, pathComparison))
            {
                continue;
            }

            if (!Directory.Exists(agentDir))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateDirectories(agentDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var skillMd = Path.Combine(entry, KnownConfigNames.SkillFileName);
                if (!File.Exists(skillMd))
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

        await Task.CompletedTask;
        return [.. skills.Values];
    }

    private static string ShortenPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..];
        }
        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(cwd) && path.StartsWith(cwd, StringComparison.Ordinal))
        {
            var relative = path[cwd.Length..];
            return "." + (relative.Length == 0 ? "" : relative);
        }
        return path;
    }

    private sealed record InstalledSkill(string Name, string CanonicalPath, List<string> Agents);
}

internal sealed record InstalledSkillJson(string Name, string Path, string Scope, string[] Agents);
