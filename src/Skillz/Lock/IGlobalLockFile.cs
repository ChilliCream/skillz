using System.Collections.Immutable;

namespace Skillz.Locking;

/// <summary>
/// Manages the global skill lock file that tracks installed skills and user preferences.
/// </summary>
internal interface IGlobalLockFile
{
    /// <summary>
    /// Reads the current lock file, returning an empty instance if it does not exist.
    /// </summary>
    Task<SkillLockFile> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the lock file, replacing any existing content.
    /// </summary>
    Task WriteAsync(SkillLockFile lockFile, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or replaces the lock entry for <paramref name="skillName"/>.
    /// </summary>
    Task AddEntryAsync(string skillName, SkillLockEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the lock entry for <paramref name="skillName"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the entry existed and was removed; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> RemoveEntryAsync(string skillName, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the lock entry for <paramref name="skillName"/>, or <see langword="null"/> if not found.
    /// </summary>
    Task<SkillLockEntry?> FindEntryAsync(string skillName, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the agents selected during the last install, or <see langword="null"/> if none have been saved.
    /// </summary>
    Task<ImmutableArray<string>?> GetLastSelectedAgentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists <paramref name="agents"/> as the most recently selected agents for future pre-selection.
    /// </summary>
    Task SaveLastSelectedAgentsAsync(ImmutableArray<string> agents, CancellationToken cancellationToken);
}
