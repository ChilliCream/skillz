using System.Collections.Immutable;

namespace Skillz.Commands;

/// <summary>
/// Drives the interactive prompts for the <c>remove</c> command (which skills to remove and
/// the final confirmation), isolating console interaction so the command can be tested without a TTY.
/// </summary>
internal interface IRemoveCommandPrompter
{
    Task<ImmutableArray<string>> SelectSkillsAsync(
        ImmutableArray<string> installed,
        CancellationToken cancellationToken);

    Task<bool> ConfirmRemovalAsync(ImmutableArray<string> skills, CancellationToken cancellationToken);
}
