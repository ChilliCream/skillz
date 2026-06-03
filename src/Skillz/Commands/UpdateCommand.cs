using System.Collections.Immutable;
using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Skills;
using Skillz.Utils;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class UpdateCommand(
    IInteractionService interaction,
    IGlobalLockFile globalLockFile,
    IProjectLockFile projectLockFile,
    IBlobClient blobClient,
    IFileStore fileStore,
    ISystemEnvironment system,
    ConsoleEnvironment consoleEnvironment) : BaseCommand("update", "Check for skill updates.")
{
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
        Aliases.Add("upgrade");
        Aliases.Add("check");
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
            parseResult.GetValue(_globalOption),
            parseResult.GetValue(_projectOption),
            parseResult.GetValue(_yesOption),
            skillFilter);

        var scope = await ResolveScopeAsync(options, cancellationToken);

        if (skillFilter is not null)
        {
            interaction.WriteMarkupLine($"Checking {Markup.Escape(skillFilter.Join(", "))}...");
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

            var globalResult = await CheckGlobalSkillsAsync(skillFilter, cancellationToken);
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

            var projectResult = await CheckProjectSkillsAsync(skillFilter, cancellationToken);
            totalFail += projectResult.FailCount;
            totalFound += projectResult.CheckedCount;
        }

        if (skillFilter is not null && totalFound == 0)
        {
            interaction.WriteDim($"No installed skills found matching: {skillFilter.Join(", ")}");
        }

        interaction.WriteLine();

        if (totalUpdatesAvailable > 0)
        {
            interaction.WriteWarning(
                $"Updates available for {totalUpdatesAvailable} skill(s); no updates were applied.");
        }

        if (totalFail > 0)
        {
            interaction.WriteDim($"Failed to check {totalFail} skill(s)");
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
            return await HasProjectSkillsAsync(cancellationToken) ? UpdateScope.Project : UpdateScope.Global;
        }

        return await interaction.SelectAsync(
            "Update scope",
            new[]
            {
                ("Project (update skills in current directory)", UpdateScope.Project),
                ("Global (update skills in home directory)", UpdateScope.Global),
                ("Both (update all skills)", UpdateScope.Both)
            },
            cancellationToken);
    }

    private Task<bool> HasProjectSkillsAsync(CancellationToken cancellationToken)
    {
        var cwd = system.CurrentDirectory;

        var skillsDir = Path.Combine(cwd, KnownConfigNames.UniversalSkillsDirectory);
        try
        {
            if (fileStore.FileExists(Path.Combine(cwd, KnownConfigNames.ProjectLockFileName)))
            {
                return Task.FromResult(true);
            }

            if (!fileStore.DirectoryExists(skillsDir))
            {
                return Task.FromResult(false);
            }

            foreach (var entry in fileStore.EnumerateDirectories(skillsDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fileStore.FileExists(Path.Combine(entry, KnownConfigNames.SkillFileName)))
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

    private async Task<(int UpdatesAvailableCount, int FailCount, int CheckedCount)> CheckGlobalSkillsAsync(
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
        var failed = new List<string>();

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

            if (latestHash is null)
            {
                failed.Add(skillName);
            }
            else if (!latestHash.EqualsOrdinal(entry.SkillFolderHash))
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

        if (updates.Count == 0 && failed.Count == 0)
        {
            interaction.WriteSuccess("All global skills are up to date");
            PrintSkippedSkills(skipped);
            return (0, 0, checkedCount);
        }

        if (updates.Count > 0)
        {
            interaction.WriteMarkupLine($"Found {updates.Count} global update(s)");
            interaction.WriteLine();

            foreach (var (name, _) in updates)
            {
                interaction.WriteMarkupLine($"[grey85]Update available:[/] {Markup.Escape(name)}");
                interaction.WriteDim($"  Run: skillz add {BuildInstallSource(lockFile.Skills[name])} -g -y");
            }
        }

        PrintSkippedSkills(skipped);
        PrintFailedSkills(failed);

        return (updates.Count, failed.Count, checkedCount);
    }

    private async Task<(int FailCount, int CheckedCount)> CheckProjectSkillsAsync(
        string[]? skillFilter,
        CancellationToken cancellationToken)
    {
        var localLock = await projectLockFile.ReadAsync(cwd: null, cancellationToken);

        var projectSkills = new List<(string Name, LocalSkillLockEntry Entry)>();
        foreach (var (name, entry) in localLock.Skills)
        {
            if (!MatchesSkillFilter(name, skillFilter))
            {
                continue;
            }

            if (entry.SourceType.EqualsOrdinal("node_modules") || entry.SourceType.EqualsOrdinal("local"))
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
            return (0, 0);
        }

        var updatable = projectSkills.Where(s => !string.IsNullOrEmpty(s.Entry.SkillPath)).ToList();
        var legacy = projectSkills.Where(s => string.IsNullOrEmpty(s.Entry.SkillPath)).ToList();

        if (updatable.Count == 0)
        {
            interaction.WriteDim("No project skills can be updated in place.");
            PrintLegacyProjectSkills(legacy);
            return (0, projectSkills.Count);
        }

        interaction.WriteDim($"{updatable.Count} project skill(s) can be refreshed (re-install to update):");
        interaction.WriteLine();

        foreach (var (name, entry) in updatable)
        {
            var installUrl = BuildLocalInstallSource(entry);
            interaction.WriteMarkupLine($"[grey85]Refresh:[/] {Markup.Escape(name)}");
            interaction.WriteDim($"  Run: skillz add {installUrl} --skill {name} -y");
        }

        PrintLegacyProjectSkills(legacy);

        return (0, projectSkills.Count);
    }

    private async Task<string?> TryFetchSkillFolderHashAsync(
        string ownerRepo,
        string skillPath,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var slash = ownerRepo.IndexOfOrdinal('/');
        if (slash <= 0 || slash == ownerRepo.Length - 1)
        {
            return null;
        }

        var owner = ownerRepo[..slash];
        var repo = ownerRepo[(slash + 1)..];

        try
        {
            var tree = await blobClient.FetchTreeAsync(owner, repo, @ref, cancellationToken);
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
                if (entry.Type.EqualsOrdinal("tree") && entry.Path.EqualsOrdinal(folderPath))
                {
                    return entry.Sha;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Honor the Try contract: any fetch failure (network, HTTP, timeout, missing
            // repo) maps to null so the caller can report "couldn't check". Only a genuine
            // user cancellation is allowed to propagate.
            return null;
        }
    }

    private static string DeriveSkillFolder(string skillPath)
    {
        const string withSlashSkillMd = "/" + KnownConfigNames.SkillFileName;
        const string skillMd = KnownConfigNames.SkillFileName;

        var folder = skillPath.Replace('\\', '/');
        if (folder.EndsWithOrdinalIgnoreCase(withSlashSkillMd))
        {
            folder = folder[..^withSlashSkillMd.Length];
        }
        else if (folder.EndsWithOrdinalIgnoreCase(skillMd))
        {
            folder = folder[..^skillMd.Length];
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
            if (name.EqualsOrdinalIgnoreCase(f))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetSkipReason(SkillLockEntry entry)
    {
        if (entry.SourceType.EqualsOrdinal("local"))
        {
            return "Local path";
        }

        if (entry.SourceType.EqualsOrdinal("git"))
        {
            return "Git URL";
        }

        if (entry.SourceType.EqualsOrdinal("well-known"))
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

    private void PrintFailedSkills(IReadOnlyList<string> failed)
    {
        if (failed.Count == 0)
        {
            return;
        }

        interaction.WriteLine();
        interaction.WriteDim($"{failed.Count} skill(s) could not be checked (network or access error):");
        foreach (var name in failed)
        {
            interaction.WriteMarkupLine($"  [grey85]*[/] {Markup.Escape(name)}");
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
        if (skill.SourceType.EqualsOrdinal("well-known"))
        {
            var idx = url.IndexOfOrdinal("/.well-known/");
            if (idx >= 0)
            {
                url = url[..idx];
            }
        }

        return url;
    }

    private static string FormatSourceInput(string sourceUrl, string? @ref)
    {
        var input = string.IsNullOrEmpty(@ref) ? sourceUrl : $"{sourceUrl}#{@ref}";
        // Display-only: the result is only ever printed via WriteDim, so strip any terminal
        // escapes (the source can embed an untrusted, caller-derived skill path).
        return TerminalSanitizer.SanitizeMetadata(input);
    }

    private static string BuildInstallSourceFolder(string source, string skillPath, string? @ref)
    {
        var folder = DeriveSkillFolder(skillPath);
        var withFolder = string.IsNullOrEmpty(folder) ? source : $"{source}/{folder}";
        var input = string.IsNullOrEmpty(@ref) ? withFolder : $"{withFolder}#{@ref}";
        // Display-only: the result is only ever printed via WriteDim. skillPath is untrusted
        // (it round-trips from an on-disk directory name), so strip terminal escapes here.
        return TerminalSanitizer.SanitizeMetadata(input);
    }

    private static string BuildInstallSource(SkillLockEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SkillPath))
        {
            return FormatSourceInput(entry.SourceUrl, entry.Ref);
        }

        return BuildInstallSourceFolder(entry.Source, entry.SkillPath, entry.Ref);
    }

    private static string BuildLocalInstallSource(LocalSkillLockEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SkillPath))
        {
            return FormatSourceInput(entry.Source, entry.Ref);
        }

        return BuildInstallSourceFolder(entry.Source, entry.SkillPath, entry.Ref);
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
