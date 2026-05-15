using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Skills;

namespace Skillz.Commands;

internal sealed class ListCommand : BaseCommand
{
    private readonly IServiceProvider _services;
    private readonly Option<bool> _globalOption;
    private readonly Option<string[]> _agentOption;
    private readonly Option<string?> _formatOption;

    public ListCommand(IServiceProvider services)
        : base("list", "List installed skills")
    {
        _services = services;

        _globalOption = new Option<bool>(CommonOptionNames.Global, "-g")
        {
            Description = "List global skills"
        };
        Options.Add(_globalOption);

        _agentOption = new Option<string[]>(CommonOptionNames.Agent, "-a")
        {
            Description = "Filter by agent",
            AllowMultipleArgumentsPerToken = true
        };
        Options.Add(_agentOption);

        _formatOption = new Option<string?>(CommonOptionNames.FormatJson)
        {
            Description = "Output format (text|json)"
        };
        Options.Add(_formatOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var global = parseResult.GetValue(_globalOption);
        var agents = parseResult.GetValue(_agentOption) ?? Array.Empty<string>();
        var format = parseResult.GetValue(_formatOption);
        var jsonOutput = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

        var installer = _services.GetRequiredService<IInstaller>();
        var registry = _services.GetRequiredService<IAgentRegistry>();
        var interaction = _services.GetRequiredService<IInteractionService>();
        var executionContext = _services.GetRequiredService<CliExecutionContext>();

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
        var agentFilter = agents.Length > 0 ? agents : registry.ListAgentTypes().ToArray();

        var skills = await CollectInstalledSkillsAsync(installer, registry, agentFilter, global, cwd, cancellationToken)
            .ConfigureAwait(false);

        if (jsonOutput)
        {
            var payload = skills.Select(s => new InstalledSkillJson(
                s.Name,
                s.CanonicalPath,
                global ? "global" : "project",
                s.Agents.Select(a => registry.TryGetConfig(a, out var c) && c is not null ? c.DisplayName : a).ToArray())).ToArray();

            var json = JsonSerializer.Serialize(payload, JsonSourceGenerationContext.Default.InstalledSkillJsonArray);
            Console.WriteLine(json);
            return new CommandResult.Success();
        }

        if (skills.Count == 0)
        {
            interaction.WriteDim(global ? "No global skills found." : "No project skills found.");
            return new CommandResult.Success();
        }

        var scopeLabel = global ? "Global" : "Project";
        interaction.WriteMarkupLine($"[bold]{scopeLabel} Skills[/]");
        interaction.WriteLine();

        foreach (var skill in skills)
        {
            var agentNames = skill.Agents.Select(a =>
                registry.TryGetConfig(a, out var c) && c is not null ? c.DisplayName : a).ToList();
            var agentDisplay = agentNames.Count > 0 ? string.Join(", ", agentNames) : "(not linked)";
            interaction.WriteMarkupLine($"[cyan]{skill.Name}[/] [dim]{skill.CanonicalPath}[/]");
            interaction.WriteDim($"  Agents: {agentDisplay}");
        }

        return new CommandResult.Success();
    }

    private static async Task<IReadOnlyList<InstalledSkill>> CollectInstalledSkillsAsync(
        IInstaller installer,
        IAgentRegistry registry,
        IReadOnlyList<string> agentFilter,
        bool global,
        string cwd,
        CancellationToken cancellationToken)
    {
        var skills = new Dictionary<string, InstalledSkill>(StringComparer.Ordinal);

        var canonicalDir = installer.GetCanonicalSkillsDir(global, cwd);
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

                skills[name] = new InstalledSkill(name, entry, new List<string>());
            }
        }

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
                    existing = new InstalledSkill(name, entry, new List<string>());
                    skills[name] = existing;
                }

                if (!existing.Agents.Contains(agentType, StringComparer.Ordinal))
                {
                    existing.Agents.Add(agentType);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return skills.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    private sealed record InstalledSkill(string Name, string CanonicalPath, List<string> Agents);
}

internal sealed record InstalledSkillJson(string Name, string Path, string Scope, string[] Agents);
