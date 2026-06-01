using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Net;
using Skillz.Skills;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class UpdateCommand(
    IInteractionService interaction,
    IGlobalLockFile globalLockFile,
    IProjectLockFile projectLockFile,
    IBlobClient blobClient,
    ConsoleEnvironment consoleEnvironment) : BaseCommand(CommandName, "Check for skill updates.")
{
    public const string CommandName = "update";

    public static readonly IReadOnlyList<string> CommandAliases = ["upgrade", "check"];

    private readonly Argument<string[]> _skillsArgument = new("skills")
    {
        Description = "Optional skill names to update.",
        Arity = ArgumentArity.ZeroOrMore
    };

    private readonly Option<bool> _globalOption = new("--global", "-g") { Description = "Update global skills only." };

    private readonly Option<bool> _projectOption = new("--project", "-p")
    {
        Description = "Update project skills only."
    };

    private readonly Option<bool> _yesOption = new("--yes", "-y") { Description = "Skip interactive prompts." };

    protected override void Configure()
    {
        foreach (var alias in CommandAliases)
        {
            Aliases.Add(alias);
        }

        Arguments.Add(_skillsArgument);
        Options.Add(_globalOption);
        Options.Add(_projectOption);
        Options.Add(_yesOption);
    }

    protected override async Task<CommandResult> ExecuteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        var skills = parseResult.GetValue(_skillsArgument);
        var skillFilter = skills is { Length: > 0 } ? skills : null;

        var options = new UpdateCheckOptions(
            Global: parseResult.GetValue(_globalOption),
            Project: parseResult.GetValue(_projectOption),
            Yes: parseResult.GetValue(_yesOption),
            Skills: skillFilter);

        var scope = await ResolveScopeAsync(options, cancellationToken);

        if (skillFilter is not null)
        {
            interaction.WriteMarkupLine($"Checking {Markup.Escape(string.Join(", ", skillFilter))}...");
        }
        else
        {
            interaction.WriteLine("Checking for skill updates...");
        }

        interaction.WriteLine();

        var totalUpdatesAvailable = 0;
        var totalFail = 0;
        var totalFound = 0;

        if (scope is UpdateScope.Global or UpdateScope.Both)
        {
            if (scope == UpdateScope.Both && skillFilter is null)
            {
                interaction.WriteMarkupLine("[bold]Global Skills[/]");
            }

            var globalResult = await UpdateGlobalSkillsAsync(skillFilter, cancellationToken);
            totalUpdatesAvailable += globalResult.UpdatesAvailableCount;
            totalFail += globalResult.FailCount;
            totalFound += globalResult.CheckedCount;

            if (scope == UpdateScope.Both && skillFilter is null)
            {
                interaction.WriteLine();
            }
        }

        if (scope is UpdateScope.Project or UpdateScope.Both)
        {
            if (scope == UpdateScope.Both && skillFilter is null)
            {
                interaction.WriteMarkupLine("[bold]Project Skills[/]");
            }

            var projectResult = await UpdateProjectSkillsAsync(skillFilter, cancellationToken);
            totalUpdatesAvailable += projectResult.UpdatesAvailableCount;
            totalFail += projectResult.FailCount;
            totalFound += projectResult.FoundCount;
        }

        if (skillFilter is not null && totalFound == 0)
        {
            interaction.WriteDim($"No installed skills found matching: {string.Join(", ", skillFilter)}");
        }

        interaction.WriteLine();

        if (totalUpdatesAvailable > 0)
        {
            interaction.WriteWarning(
                $"Updates available for {totalUpdatesAvailable} skill(s); no updates were applied.");
        }

        if (totalFail > 0)
        {
            interaction.WriteDim($"Failed to update {totalFail} skill(s)");
        }

        interaction.WriteLine();
        return new CommandResult.Success();
    }

    private async Task<UpdateScope> ResolveScopeAsync(UpdateCheckOptions options, CancellationToken cancellationToken)
    {
        if (options.Skills is { Length: > 0 })
        {
            if (options.Global)
            {
                return UpdateScope.Global;
            }

            if (options.Project)
            {
                return UpdateScope.Project;
            }

            return UpdateScope.Both;
        }

        if (options.Global && options.Project)
        {
            return UpdateScope.Both;
        }

        if (options.Global)
        {
            return UpdateScope.Global;
        }

        if (options.Project)
        {
            return UpdateScope.Project;
        }

        if (options.Yes || consoleEnvironment.IsInputRedirected)
        {
            return await HasProjectSkillsAsync(cancellationToken)
                ? UpdateScope.Project
                : UpdateScope.Global;
        }

        var choice = await interaction
            .SelectAsync(
                "Update scope",
                new[]
                {
                    ("Project (update skills in current directory)", UpdateScope.Project),
                    ("Global (update skills in home directory)", UpdateScope.Global),
                    ("Both (update all skills)", UpdateScope.Both)
                },
                cancellationToken);

        return choice;
    }

    private static Task<bool> HasProjectSkillsAsync(CancellationToken cancellationToken)
    {
        var cwd = Directory.GetCurrentDirectory();

        if (File.Exists(Path.Combine(cwd, KnownConfigNames.ProjectLockFileName)))
        {
            return Task.FromResult(true);
        }

        var skillsDir = Path.Combine(cwd, KnownConfigNames.UniversalSkillsDir);
        try
        {
            if (!Directory.Exists(skillsDir))
            {
                return Task.FromResult(false);
            }

            foreach (var entry in Directory.EnumerateDirectories(skillsDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(Path.Combine(entry, KnownConfigNames.SkillFileName)))
                {
                    return Task.FromResult(true);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(false);
    }

    private async Task<(int UpdatesAvailableCount, int FailCount, int CheckedCount)> UpdateGlobalSkillsAsync(
        string[]? skillFilter,
        CancellationToken cancellationToken)
    {
        var lockFile = await globalLockFile.ReadAsync(cancellationToken);

        if (lockFile.Skills.Count == 0)
        {
            if (skillFilter is null)
            {
                interaction.WriteDim("No global skills tracked in lock file.");
                interaction.WriteDim("Install skills with: skillz add <package> -g");
            }
            return (0, 0, 0);
        }

        var checkable = new List<(string Name, SkillLockEntry Entry)>();
        var skipped = new List<SkippedSkill>();
        var updates = new List<(string Name, SkillLockEntry Entry)>();

        foreach (var (name, entry) in lockFile.Skills)
        {
            if (!MatchesSkillFilter(name, skillFilter))
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.SkillFolderHash) || string.IsNullOrEmpty(entry.SkillPath))
            {
                skipped.Add(new SkippedSkill(name, GetSkipReason(entry), entry.SourceUrl, entry.SourceType, entry.Ref));
                continue;
            }

            checkable.Add((name, entry));
        }

        for (var i = 0; i < checkable.Count; i++)
        {
            var (skillName, entry) = checkable[i];
            var progressText =
                $"Checking global skill {i + 1}/{checkable.Count}: {TerminalSanitizer.SanitizeMetadata(skillName)}";
            if (!consoleEnvironment.IsInputRedirected && consoleEnvironment.IsTty)
            {
                Console.Write($"\r\x1b[K{progressText}");
            }
            else
            {
                interaction.WriteDim(progressText);
            }

            var latestHash = await TryFetchSkillFolderHashAsync(
                    entry.Source,
                    entry.SkillPath!,
                    entry.Ref,
                    cancellationToken);
            if (latestHash is not null
                && !string.Equals(latestHash, entry.SkillFolderHash, StringComparison.Ordinal))
            {
                updates.Add((skillName, entry));
            }
        }

        if (!consoleEnvironment.IsInputRedirected
            && consoleEnvironment.IsTty
            && checkable.Count > 0)
        {
            Console.Write("\r\x1b[K");
        }

        var checkedCount = checkable.Count + skipped.Count;

        if (checkable.Count == 0 && skipped.Count == 0)
        {
            if (skillFilter is null)
            {
                interaction.WriteDim("No global skills to check.");
            }
            return (0, 0, 0);
        }

        if (checkable.Count == 0 && skipped.Count > 0)
        {
            PrintSkippedSkills(skipped);
            return (0, 0, checkedCount);
        }

        if (updates.Count == 0)
        {
            interaction.WriteSuccess("All global skills are up to date");
            return (0, 0, checkedCount);
        }

        interaction.WriteMarkupLine($"Found {updates.Count} global update(s)");
        interaction.WriteLine();

        var updatesAvailableCount = 0;

        foreach (var (name, _) in updates)
        {
            interaction.WriteMarkupLine($"[grey85]Update available:[/] {Markup.Escape(name)}");
            interaction.WriteDim($"  Run: skillz add {BuildInstallSource(lockFile.Skills[name])} -g -y");
            updatesAvailableCount++;
        }

        PrintSkippedSkills(skipped);
        return (updatesAvailableCount, 0, checkedCount);
    }

    private async Task<(int UpdatesAvailableCount, int FailCount, int FoundCount)> UpdateProjectSkillsAsync(
        string[]? skillFilter,
        CancellationToken cancellationToken)
    {
        var localLock = await projectLockFile.ReadAsync(cancellationToken: cancellationToken);

        var projectSkills = new List<(string Name, LocalSkillLockEntry Entry)>();
        foreach (var (name, entry) in localLock.Skills)
        {
            if (!MatchesSkillFilter(name, skillFilter))
            {
                continue;
            }

            if (string.Equals(entry.SourceType, "node_modules", StringComparison.Ordinal)
                || string.Equals(entry.SourceType, "local", StringComparison.Ordinal))
            {
                continue;
            }
            projectSkills.Add((name, entry));
        }

        if (projectSkills.Count == 0)
        {
            if (skillFilter is null)
            {
                interaction.WriteDim("No project skills to update.");
                interaction.WriteDim("Install project skills with: skillz add <package>");
            }
            return (0, 0, 0);
        }

        var updatable = projectSkills.Where(s => !string.IsNullOrEmpty(s.Entry.SkillPath)).ToList();
        var legacy = projectSkills.Where(s => string.IsNullOrEmpty(s.Entry.SkillPath)).ToList();

        if (updatable.Count == 0)
        {
            interaction.WriteDim("No project skills can be updated in place.");
            PrintLegacyProjectSkills(legacy);
            return (0, 0, projectSkills.Count);
        }

        interaction.WriteMarkupLine($"Found {updatable.Count} project update(s)");
        interaction.WriteLine();

        var updatesAvailableCount = 0;

        foreach (var (name, entry) in updatable)
        {
            var installUrl = BuildLocalInstallSource(entry);
            interaction.WriteMarkupLine($"[grey85]Update available:[/] {Markup.Escape(name)}");
            interaction.WriteDim($"  Run: skillz add {installUrl} --skill {name} -y");
            updatesAvailableCount++;
        }

        PrintLegacyProjectSkills(legacy);
        await Task.CompletedTask;

        return (updatesAvailableCount, 0, projectSkills.Count);
    }

    private async Task<string?> TryFetchSkillFolderHashAsync(
        string ownerRepo,
        string skillPath,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var slash = ownerRepo.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == ownerRepo.Length - 1)
        {
            return null;
        }

        var owner = ownerRepo[..slash];
        var repo = ownerRepo[(slash + 1)..];

        try
        {
            var tree = await blobClient
                .FetchTreeAsync(owner, repo, @ref, path: null, cancellationToken);
            if (tree is null)
            {
                return null;
            }

            var folderPath = DeriveSkillFolder(skillPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                return tree.Sha;
            }

            foreach (var entry in tree.Tree)
            {
                if (string.Equals(entry.Type, "tree", StringComparison.Ordinal)
                    && string.Equals(entry.Path, folderPath, StringComparison.Ordinal))
                {
                    return entry.Sha;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string DeriveSkillFolder(string skillPath)
    {
        var folder = skillPath.Replace('\\', '/');
        if (folder.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            folder = folder[..^9];
        }
        else if (folder.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            folder = folder[..^8];
        }

        if (folder.EndsWith('/'))
        {
            folder = folder[..^1];
        }

        return folder;
    }

    private static bool MatchesSkillFilter(string name, string[]? filter)
    {
        if (filter is null || filter.Length == 0)
        {
            return true;
        }

        foreach (var f in filter)
        {
            if (string.Equals(name, f, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetSkipReason(SkillLockEntry entry)
    {
        if (string.Equals(entry.SourceType, "local", StringComparison.Ordinal))
        {
            return "Local path";
        }

        if (string.Equals(entry.SourceType, "git", StringComparison.Ordinal))
        {
            return "Git URL";
        }

        if (string.Equals(entry.SourceType, "well-known", StringComparison.Ordinal))
        {
            return "Well-known skill";
        }

        if (string.IsNullOrEmpty(entry.SkillFolderHash))
        {
            return "Private or deleted repo";
        }

        if (string.IsNullOrEmpty(entry.SkillPath))
        {
            return "No skill path recorded";
        }

        return "No version tracking";
    }

    private void PrintSkippedSkills(IReadOnlyList<SkippedSkill> skipped)
    {
        if (skipped.Count == 0)
        {
            return;
        }

        interaction.WriteLine();
        interaction.WriteDim($"{skipped.Count} skill(s) cannot be checked automatically:");
        foreach (var skill in skipped)
        {
            interaction.WriteMarkupLine(
                $"  [grey85]*[/] {Markup.Escape(skill.Name)} [dim]({Markup.Escape(skill.Reason)})[/]");
            var manual = FormatSourceInput(GetInstallSource(skill), skill.Ref);
            interaction.WriteDim($"    To update: skillz add {manual} -g -y");
        }
    }

    private void PrintLegacyProjectSkills(IReadOnlyList<(string Name, LocalSkillLockEntry Entry)> legacy)
    {
        if (legacy.Count == 0)
        {
            return;
        }

        interaction.WriteLine();
        interaction.WriteDim(
            $"{legacy.Count} project skill(s) cannot be updated automatically (installed before skillPath tracking):");
        foreach (var (name, entry) in legacy)
        {
            var reinstall = FormatSourceInput(entry.Source, entry.Ref);
            interaction.WriteMarkupLine($"  [grey85]*[/] {Markup.Escape(name)}");
            interaction.WriteDim($"    To refresh: skillz add {reinstall} -y");
        }
    }

    private static string GetInstallSource(SkippedSkill skill)
    {
        var url = skill.SourceUrl;
        if (string.Equals(skill.SourceType, "well-known", StringComparison.Ordinal))
        {
            var idx = url.IndexOf("/.well-known/", StringComparison.Ordinal);
            if (idx >= 0)
            {
                url = url[..idx];
            }
        }

        return url;
    }

    private static string FormatSourceInput(string sourceUrl, string? @ref)
    {
        return string.IsNullOrEmpty(@ref) ? sourceUrl : $"{sourceUrl}#{@ref}";
    }

    private static string BuildInstallSource(SkillLockEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SkillPath))
        {
            return FormatSourceInput(entry.SourceUrl, entry.Ref);
        }

        var folder = DeriveSkillFolder(entry.SkillPath);
        var withFolder = string.IsNullOrEmpty(folder) ? entry.Source : $"{entry.Source}/{folder}";
        return string.IsNullOrEmpty(entry.Ref) ? withFolder : $"{withFolder}#{entry.Ref}";
    }

    private static string BuildLocalInstallSource(LocalSkillLockEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SkillPath))
        {
            return FormatSourceInput(entry.Source, entry.Ref);
        }

        var folder = DeriveSkillFolder(entry.SkillPath);
        var withFolder = string.IsNullOrEmpty(folder) ? entry.Source : $"{entry.Source}/{folder}";
        return string.IsNullOrEmpty(entry.Ref) ? withFolder : $"{withFolder}#{entry.Ref}";
    }

    private sealed record UpdateCheckOptions(bool Global, bool Project, bool Yes, string[]? Skills);

    private sealed record SkippedSkill(string Name, string Reason, string SourceUrl, string SourceType, string? Ref);

    private enum UpdateScope
    {
        Project,
        Global,
        Both
    }
}
