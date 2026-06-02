using Skillz.Locking;

namespace Skillz.Tests.TestServices;

internal sealed class TestProjectLockFile : IProjectLockFile
{
    public Func<string?, string>? OnGetLockPath { get; set; }

    public Func<string?, LocalSkillLockFile>? OnRead { get; set; }

    public Action<LocalSkillLockFile, string?>? OnWrite { get; set; }

    public Action<string, LocalSkillLockEntry, string?>? OnAddEntry { get; set; }

    public Func<string, string?, bool>? OnRemoveEntry { get; set; }

    public Func<string, string?, bool>? OnHasSkill { get; set; }

    public Func<string, string>? OnComputeSkillFolderHash { get; set; }

    public string GetLockPath(string? cwd = null)
    {
        return OnGetLockPath is not null
            ? OnGetLockPath(cwd)
            : Path.Combine(cwd ?? Directory.GetCurrentDirectory(), "skillz.lock");
    }

    public Task<LocalSkillLockFile> ReadAsync(string? cwd, CancellationToken cancellationToken)
    {
        var result = OnRead is not null ? OnRead(cwd) : new LocalSkillLockFile { Version = 1 };
        return Task.FromResult(result);
    }

    public Task WriteAsync(
        LocalSkillLockFile lockFile,
        string? cwd,
        CancellationToken cancellationToken)
    {
        OnWrite?.Invoke(lockFile, cwd);
        return Task.CompletedTask;
    }

    public Task AddEntryAsync(
        string skillName,
        LocalSkillLockEntry entry,
        string? cwd,
        CancellationToken cancellationToken)
    {
        OnAddEntry?.Invoke(skillName, entry, cwd);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveEntryAsync(
        string skillName,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var result = OnRemoveEntry is not null && OnRemoveEntry(skillName, cwd);
        return Task.FromResult(result);
    }

    public Task<bool> HasSkillAsync(string skillName, string? cwd, CancellationToken cancellationToken)
    {
        var result = OnHasSkill is not null && OnHasSkill(skillName, cwd);
        return Task.FromResult(result);
    }

    public Task<string> ComputeSkillFolderHashAsync(
        string skillDirectory,
        CancellationToken cancellationToken)
    {
        var result = OnComputeSkillFolderHash is not null ? OnComputeSkillFolderHash(skillDirectory) : string.Empty;
        return Task.FromResult(result);
    }
}
