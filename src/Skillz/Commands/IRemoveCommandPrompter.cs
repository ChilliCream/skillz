using System.Collections.Immutable;

namespace Skillz.Commands;

internal interface IRemoveCommandPrompter
{
    Task<ImmutableArray<string>> SelectSkillsAsync(
        ImmutableArray<string> installed,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmRemovalAsync(ImmutableArray<string> skills, CancellationToken cancellationToken = default);
}
