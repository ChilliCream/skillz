using System.Collections.Immutable;
using Skillz.Locking;

namespace Skillz.Tests.TestServices;

internal sealed class TestGlobalLockFile : IGlobalLockFile
{
    public Func<SkillLockFile>? OnRead { get; set; }

    public Action<SkillLockFile>? OnWrite { get; set; }

    public Action<string, SkillLockEntry>? OnAddEntry { get; set; }

    public Func<string, bool>? OnRemoveEntry { get; set; }

    public Func<string, SkillLockEntry?>? OnGetEntry { get; set; }

    public Task<SkillLockFile> ReadAsync(CancellationToken cancellationToken)
    {
        var result = OnRead is not null ? OnRead() : new SkillLockFile { Version = 1 };
        return Task.FromResult(result);
    }

    public Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken)
    {
        OnWrite?.Invoke(lockFile);
        return Task.CompletedTask;
    }

    public Task AddEntryAsync(string skillName, SkillLockEntry entry, CancellationToken cancellationToken)
    {
        OnAddEntry?.Invoke(skillName, entry);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveEntryAsync(string skillName, CancellationToken cancellationToken)
    {
        var result = OnRemoveEntry is not null && OnRemoveEntry(skillName);
        return Task.FromResult(result);
    }

    public Task<SkillLockEntry?> FindEntryAsync(string skillName, CancellationToken cancellationToken)
    {
        var result = OnGetEntry?.Invoke(skillName);
        return Task.FromResult(result);
    }

    public Func<IReadOnlyList<string>?>? OnGetLastSelectedAgents { get; set; }

    public Action<IReadOnlyList<string>>? OnSaveLastSelectedAgents { get; set; }

    public Task<ImmutableArray<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken)
    {
        var result = OnGetLastSelectedAgents?.Invoke();
        return Task.FromResult<ImmutableArray<string>?>(result is null ? null : [.. result]);
    }

    public Task SaveLastSelectedAgentsAsync(ImmutableArray<string> agents, CancellationToken cancellationToken)
    {
        OnSaveLastSelectedAgents?.Invoke(agents);
        return Task.CompletedTask;
    }
}
