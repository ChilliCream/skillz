using System.Collections.Immutable;

namespace Skillz.Locking;

internal interface IGlobalLockFile
{
    Task<SkillLockFile> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken);

    Task AddEntryAsync(string skillName, SkillLockEntry entry, CancellationToken cancellationToken);

    Task<bool> RemoveEntryAsync(string skillName, CancellationToken cancellationToken);

    Task<SkillLockEntry?> GetEntryAsync(string skillName, CancellationToken cancellationToken);

    Task<ImmutableArray<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken);

    Task SaveLastSelectedAgentsAsync(ImmutableArray<string> agents, CancellationToken cancellationToken);
}
