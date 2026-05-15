namespace Skillz.Commands;

internal interface IRemoveCommandPrompter
{
    Task<IReadOnlyList<string>> SelectSkillsAsync(
        IReadOnlyList<string> installed,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmRemovalAsync(
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default);
}
