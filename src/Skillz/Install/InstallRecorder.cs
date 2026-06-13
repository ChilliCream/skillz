using Skillz.Git;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Sources;
using Skillz.Utils;
using Spectre.Console;

namespace Skillz.Install;

internal sealed class InstallRecorder(
    IProjectLockFile projectLock,
    IGlobalLockFile globalLock,
    IFileStore fileStore,
    TimeProvider timeProvider,
    IAnsiConsole console)
{
    public async Task RecordAsync(InstallReport report, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime.ToString("o");
        // Never persist embedded credentials: strip any userinfo before the URL reaches the lock
        // file. Re-clones during update authenticate via git's own credential helpers, not via a
        // token baked into the stored URL, so a credential-free URL still works for private repos.
        var sourceUrl = GitUrl.StripUserInfo(report.Source.Url);
        var sourceType = report.Source.SourceType;
        var refValue = report.Source.Ref;
        var ownerRepo = OwnerRepoParser.FindOwnerRepo(report.Source);
        var source = ownerRepo ?? sourceUrl;

        var bySkill = report.Successful
            .GroupBy(r => r.Skill.InstallName, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        foreach (var entry in bySkill)
        {
            var installPath = entry.Result.Path;
            if (report.InstallGlobally)
            {
                // Global-lock entries are only recorded for owner/repo sources; URL-only
                // installs are skipped here, so don't compute a hash we'd throw away.
                if (string.IsNullOrEmpty(ownerRepo))
                {
                    continue;
                }

                var skillFolderHash = await ComputeHashAsync(installPath, cancellationToken);

                var lockEntry = new SkillLockEntry
                {
                    Source = source,
                    SourceType = sourceType,
                    SourceUrl = sourceUrl,
                    Ref = refValue,
                    SkillPath = entry.Skill.SkillPath,
                    SkillFolderHash = skillFolderHash,
                    InstalledAt = now,
                    UpdatedAt = now
                };
                try
                {
                    await globalLock.AddEntryAsync(entry.Skill.InstallName, lockEntry, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    console.Warning(
                        $"Could not record lock entry for '{entry.Skill.InstallName}': {ex.Message}");
                }
            }
            else
            {
                var computedHash = await ComputeHashAsync(installPath, cancellationToken);

                var lockEntry = new LocalSkillLockEntry
                {
                    Source = source,
                    SourceType = sourceType,
                    Ref = refValue,
                    SkillPath = entry.Skill.SkillPath,
                    ComputedHash = computedHash
                };
                try
                {
                    await projectLock.AddEntryAsync(entry.Skill.InstallName, lockEntry, cwd: null, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    console.Warning(
                        $"Could not record lock entry for '{entry.Skill.InstallName}': {ex.Message}");
                }
            }
        }
    }

    private async Task<string> ComputeHashAsync(string? path, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && fileStore.DirectoryExists(path))
            {
                return await projectLock.ComputeSkillFolderHashAsync(path, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hash is advisory; on failure we fall back to an empty hash.
        }

        return string.Empty;
    }
}
