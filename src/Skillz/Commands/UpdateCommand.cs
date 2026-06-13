using System.Collections.Immutable;
using System.CommandLine;
using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Skillz.Interaction.Prompts;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Skills;
using Skillz.Utils;
using Skillz.Views;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class UpdateCommand(
    IAnsiConsole console,
    IGlobalLockFile globalLockFile,
    IProjectLockFile projectLockFile,
    IBlobClient blobClient,
    ConsoleEnvironment consoleEnvironment) : BaseCommand(console, "update", "Check for skill updates.")
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

    protected override async Task<int> ExecuteAsync(
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
            Output.WriteLine($"Checking {skillFilter.Join(", ")}...");
        }
        else
        {
            Output.WriteLine("Checking for skill updates...");
        }

        Output.WriteLine();

        var totalUpdatesAvailable = 0;
        var totalFail = 0;
        var totalFound = 0;

        if (scope is UpdateScope.Global or UpdateScope.Both)
        {
            if (scope == UpdateScope.Both && skillFilter is null)
            {
                Output.MarkupLineRaw("[bold]Global Skills[/]");
            }

            var globalResult = await CheckGlobalSkillsAsync(skillFilter, cancellationToken);
            totalUpdatesAvailable += globalResult.UpdatesAvailableCount;
            totalFail += globalResult.FailCount;
            totalFound += globalResult.CheckedCount;

            if (scope == UpdateScope.Both && skillFilter is null)
            {
                Output.WriteLine();
            }
        }

        if (scope is UpdateScope.Project or UpdateScope.Both)
        {
            if (scope == UpdateScope.Both && skillFilter is null)
            {
                Output.MarkupLineRaw("[bold]Project Skills[/]");
            }

            var projectResult = await CheckProjectSkillsAsync(skillFilter, cancellationToken);
            totalFail += projectResult.FailCount;
            totalFound += projectResult.CheckedCount;
        }

        if (skillFilter is not null && totalFound == 0)
        {
            Output.Dim($"No installed skills found matching: {skillFilter.Join(", ")}");
        }

        Output.WriteLine();

        if (totalUpdatesAvailable > 0)
        {
            Output.Warning(
                $"Updates available for {totalUpdatesAvailable} skill(s); no updates were applied.");
        }

        if (totalFail > 0)
        {
            Output.Dim($"Failed to check {totalFail} skill(s)");
        }

        Output.WriteLine();
        return ExitCodeConstants.Success;
    }

    private async Task<UpdateScope> ResolveScopeAsync(UpdateCheckOptions options, CancellationToken cancellationToken)
    {
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

        // Explicit skill names without a scope flag target both scopes, and never prompt: the
        // names themselves disambiguate which skills to check across global and project.
        if (options.Skills is { Length: > 0 })
        {
            return UpdateScope.Both;
        }

        // No explicit scope flag. In non-interactive mode (an explicit -y or redirected input)
        // we cannot ask the user, and there is no reliable, side-effect-free way to know whether
        // the current directory has project skills (they may live in agent-specific dirs that the
        // command does not track). Rather than silently guess one scope and risk checking the
        // wrong one, default to checking BOTH global and project skills. This is the safe,
        // predictable choice: nothing is ever silently mis-scoped, and a redundant scope merely
        // reports "no skills" for the empty side.
        if (options.Yes || consoleEnvironment.IsInputRedirected)
        {
            return UpdateScope.Both;
        }

        // WithDefault checks both scopes when the console cannot show the picker (redirected or
        // non-ANSI), matching the default taken on the non-interactive path above.
        return await new SelectPrompt<UpdateScope>(
                "Update scope",
                new[]
                {
                    ("Project (update skills in current directory)", UpdateScope.Project),
                    ("Global (update skills in home directory)", UpdateScope.Global),
                    ("Both (update all skills)", UpdateScope.Both)
                })
            .WithDefault(defaultValue: UpdateScope.Both)
            .ShowAsync(Output, cancellationToken);
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
                Output.Dim("No global skills tracked in lock file.");
                Output.Dim("Install skills with: skillz add <package> -g");
            }
            return (0, 0, 0);
        }

        var checkable = new List<(string Name, SkillLockEntry Entry)>();
        var skipped = new List<SkippedSkill>();
        var updates = new List<(string Name, SkillLockEntry Entry)>();
        var failed = new List<string>();
        var timedOut = new List<string>();

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

        // Whether progress is drawn as an in-place TTY line (\r) rather than plain log lines.
        // When true, the line MUST be cleared once checking ends - including on cancellation or
        // an unexpected exception mid-loop - so no stale partial progress text is left behind.
        var inlineProgress = !consoleEnvironment.IsInputRedirected
            && consoleEnvironment.IsTty
            && checkable.Count > 0;

        try
        {
            for (var i = 0; i < checkable.Count; i++)
            {
                var (skillName, entry) = checkable[i];
                var progressText =
                    $"Checking global skill {i + 1}/{checkable.Count}: {TerminalSanitizer.SanitizeMetadata(skillName)}";
                if (inlineProgress)
                {
                    System.Console.Write($"\r\x1b[K{progressText}");
                }
                else
                {
                    Output.Dim(progressText);
                }

                var check = await TryFetchSkillFolderHashAsync(
                    entry.Source,
                    entry.SkillPath!,
                    entry.Ref,
                    cancellationToken);

                switch (check)
                {
                    case HashCheck.TimedOut:
                        timedOut.Add(skillName);
                        break;
                    case HashCheck.Missing:
                        failed.Add(skillName);
                        break;
                    case HashCheck.Found found when !found.Hash.EqualsOrdinal(entry.SkillFolderHash):
                        updates.Add((skillName, entry));
                        break;
                }
            }
        }
        finally
        {
            if (inlineProgress)
            {
                System.Console.Write("\r\x1b[K");
            }
        }

        var checkedCount = checkable.Count + skipped.Count;

        if (checkable.Count == 0 && skipped.Count == 0)
        {
            if (skillFilter is null)
            {
                Output.Dim("No global skills to check.");
            }
            return (0, 0, 0);
        }

        if (checkable.Count == 0 && skipped.Count > 0)
        {
            PrintSkippedSkills(skipped);
            return (0, 0, checkedCount);
        }

        if (updates.Count == 0 && failed.Count == 0 && timedOut.Count == 0)
        {
            Output.Success("All global skills are up to date");
            PrintSkippedSkills(skipped);
            return (0, 0, checkedCount);
        }

        if (updates.Count > 0)
        {
            Output.WriteLine($"Found {updates.Count} global update(s)");
            Output.WriteLine();

            foreach (var (name, _) in updates)
            {
                Output.MarkupLineRaw($"[grey85]Update available:[/] {Markup.Escape(name)}");
                Output.Dim($"  Run: skillz add {BuildInstallSource(lockFile.Skills[name])} -g -y");
            }
        }

        PrintSkippedSkills(skipped);
        PrintFailedSkills(failed);
        PrintTimedOutSkills(timedOut);

        return (updates.Count, failed.Count + timedOut.Count, checkedCount);
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
                Output.Dim("No project skills to update.");
                Output.Dim("Install project skills with: skillz add <package>");
            }
            return (0, 0);
        }

        var updatable = projectSkills.Where(s => !string.IsNullOrEmpty(s.Entry.SkillPath)).ToList();
        var legacy = projectSkills.Where(s => string.IsNullOrEmpty(s.Entry.SkillPath)).ToList();

        if (updatable.Count == 0)
        {
            Output.Dim("No project skills can be updated in place.");
            PrintLegacyProjectSkills(legacy);
            return (0, projectSkills.Count);
        }

        Output.Dim($"{updatable.Count} project skill(s) can be refreshed (re-install to update):");
        Output.WriteLine();

        foreach (var (name, entry) in updatable)
        {
            var installUrl = BuildLocalInstallSource(entry);
            Output.MarkupLineRaw($"[grey85]Refresh:[/] {Markup.Escape(name)}");
            Output.Dim($"  Run: skillz add {installUrl} --skill {name} -y");
        }

        PrintLegacyProjectSkills(legacy);

        return (0, projectSkills.Count);
    }

    private async Task<HashCheck> TryFetchSkillFolderHashAsync(
        string ownerRepo,
        string skillPath,
        string? @ref,
        CancellationToken cancellationToken)
    {
        var slash = ownerRepo.IndexOfOrdinal('/');
        if (slash <= 0 || slash == ownerRepo.Length - 1)
        {
            return HashCheck.Missing.Instance;
        }

        var owner = ownerRepo[..slash];
        var repo = ownerRepo[(slash + 1)..];

        try
        {
            var tree = await blobClient.FetchTreeAsync(owner, repo, @ref, cancellationToken);
            if (tree is null)
            {
                return HashCheck.Missing.Instance;
            }

            var folderPath = DeriveSkillFolder(skillPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                return new HashCheck.Found(tree.Sha);
            }

            foreach (var entry in tree.Tree)
            {
                if (entry.Type.EqualsOrdinal("tree") && entry.Path.EqualsOrdinal(folderPath))
                {
                    return new HashCheck.Found(entry.Sha);
                }
            }

            return HashCheck.Missing.Instance;
        }
        catch (BlobFetchTimeoutException)
        {
            // A genuine fetch timeout is distinct from "missing/private repo": surface it so
            // the caller reports a timeout rather than silently bucketing it as not-found.
            return HashCheck.TimedOut.Instance;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Other fetch failures (network, HTTP, parse) map to "missing/error". Only a
            // genuine user cancellation is allowed to propagate.
            return HashCheck.Missing.Instance;
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

        var items = skipped
            .Select(skill => new UpdateNotice(
                skill.Name,
                skill.Reason,
                $"To update: skillz add {FormatSourceInput(GetInstallSource(skill), skill.Ref)} -g -y"))
            .ToList();

        Output.WriteLine();
        Output.Write(UpdateNoticeView.Create(
            $"{skipped.Count} skill(s) cannot be checked automatically:", items));
    }

    private void PrintFailedSkills(IReadOnlyList<string> failed)
    {
        if (failed.Count == 0)
        {
            return;
        }

        Output.WriteLine();
        Output.Write(UpdateNoticeView.Create(
            $"{failed.Count} skill(s) could not be checked (network or access error):",
            failed.Select(name => new UpdateNotice(name)).ToList()));
    }

    private void PrintTimedOutSkills(List<string> timedOut)
    {
        if (timedOut.Count == 0)
        {
            return;
        }

        Output.WriteLine();
        Output.Write(UpdateNoticeView.Create(
            $"{timedOut.Count} skill(s) timed out before they could be checked:",
            timedOut.Select(name => new UpdateNotice(name)).ToList()));
    }

    private void PrintLegacyProjectSkills(IReadOnlyList<(string Name, LocalSkillLockEntry Entry)> legacy)
    {
        if (legacy.Count == 0)
        {
            return;
        }

        var items = legacy
            .Select(skill => new UpdateNotice(
                skill.Name,
                Action: $"To refresh: skillz add {FormatSourceInput(skill.Entry.Source, skill.Entry.Ref)} -y"))
            .ToList();

        Output.WriteLine();
        Output.Write(UpdateNoticeView.Create(
            $"{legacy.Count} project skill(s) cannot be updated automatically (installed before skillPath tracking):",
            items));
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
        // Display-only: the result is only ever printed as dimmed text, so strip any terminal
        // escapes (the source can embed an untrusted, caller-derived skill path).
        return TerminalSanitizer.SanitizeMetadata(input);
    }

    private static string BuildInstallSourceFolder(string source, string skillPath, string? @ref)
    {
        var folder = DeriveSkillFolder(skillPath);
        var withFolder = string.IsNullOrEmpty(folder) ? source : $"{source}/{folder}";
        var input = string.IsNullOrEmpty(@ref) ? withFolder : $"{withFolder}#{@ref}";
        // Display-only: the result is only ever printed as dimmed text. skillPath is untrusted
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

    private abstract record HashCheck
    {
        public sealed record Found(string Hash) : HashCheck;

        public sealed record Missing : HashCheck
        {
            public static readonly Missing Instance = new();
        }

        public sealed record TimedOut : HashCheck
        {
            public static readonly TimedOut Instance = new();
        }
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
