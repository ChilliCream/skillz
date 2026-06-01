using System.Collections.Immutable;
using Skillz.Interaction;

namespace Skillz.Commands;

internal sealed class RemoveCommandPrompter : IRemoveCommandPrompter
{
    private readonly IInteractionService _interaction;

    public RemoveCommandPrompter(IInteractionService interaction)
    {
        _interaction = interaction;
    }

    public async Task<ImmutableArray<string>> SelectSkillsAsync(
        ImmutableArray<string> installed,
        CancellationToken cancellationToken = default)
    {
        if (installed.Length == 0)
        {
            return [];
        }

        var choices = installed.Select(s => (s, s));
        return await _interaction
            .MultiSelectAsync("Select skills to remove", choices, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> ConfirmRemovalAsync(ImmutableArray<string> skills, CancellationToken cancellationToken = default)
    {
        var skillNames = string.Join(", ", skills);
        var message = $"Are you sure you want to remove {skills.Length} skill(s) [{skillNames}]?";
        return _interaction.ConfirmAsync(message, defaultValue: false, cancellationToken);
    }
}
