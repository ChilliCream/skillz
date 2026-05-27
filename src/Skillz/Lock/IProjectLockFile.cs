namespace Skillz.Lock;

internal interface IProjectLockFile
{
    string GetLockPath(string? cwd = null);

    Task<LocalSkillLockFile> ReadAsync(string? cwd = null, CancellationToken cancellationToken = default);

    Task WriteAsync(LocalSkillLockFile lockFile, string? cwd = null, CancellationToken cancellationToken = default);

    Task AddEntryAsync(
        string skillName,
        LocalSkillLockEntry entry,
        string? cwd = null,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveEntryAsync(string skillName, string? cwd = null, CancellationToken cancellationToken = default);

    Task<bool> HasSkillAsync(string skillName, string? cwd = null, CancellationToken cancellationToken = default);

    Task<string> ComputeSkillFolderHashAsync(string skillDirectory, CancellationToken cancellationToken = default);
}
