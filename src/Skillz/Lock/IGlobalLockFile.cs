using System.Collections.Immutable;

namespace Skillz.Lock;

internal interface IGlobalLockFile
{
    Task<SkillLockFile> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken = default);

    Task AddEntryAsync(string skillName, SkillLockEntry entry, CancellationToken cancellationToken = default);

    Task<bool> RemoveEntryAsync(string skillName, CancellationToken cancellationToken = default);

    Task<SkillLockEntry?> GetEntryAsync(string skillName, CancellationToken cancellationToken = default);

    Task<ImmutableArray<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken = default);

    Task SaveLastSelectedAgentsAsync(ImmutableArray<string> agents, CancellationToken cancellationToken = default);
}
