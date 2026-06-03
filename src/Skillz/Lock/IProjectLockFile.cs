namespace Skillz.Locking;

/// <summary>
/// Manages the per-project lock file (in the current working directory) that records the
/// origin and content hash of each project-scoped skill so updates can be detected.
/// </summary>
internal interface IProjectLockFile
{
    string GetLockPath(string? cwd = null);

    Task<LocalSkillLockFile> ReadAsync(string? cwd, CancellationToken cancellationToken);

    Task WriteAsync(LocalSkillLockFile lockFile, string? cwd, CancellationToken cancellationToken);

    Task AddEntryAsync(
        string skillName,
        LocalSkillLockEntry entry,
        string? cwd,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the lock entry for <paramref name="skillName"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the entry existed and was removed; otherwise <see langword="false"/>.</returns>
    Task<bool> RemoveEntryAsync(string skillName, string? cwd, CancellationToken cancellationToken);

    Task<bool> HasSkillAsync(string skillName, string? cwd, CancellationToken cancellationToken);

    Task<string> ComputeSkillFolderHashAsync(string skillDirectory, CancellationToken cancellationToken);
}
