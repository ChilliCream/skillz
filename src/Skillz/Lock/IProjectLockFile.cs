namespace Skillz.Locking;

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

    Task<bool> RemoveEntryAsync(string skillName, string? cwd, CancellationToken cancellationToken);

    Task<bool> HasSkillAsync(string skillName, string? cwd, CancellationToken cancellationToken);

    Task<string> ComputeSkillFolderHashAsync(string skillDirectory, CancellationToken cancellationToken);
}
