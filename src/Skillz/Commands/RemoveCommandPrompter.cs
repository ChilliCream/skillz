using System.Collections.Immutable;
using Skillz.Interaction;

namespace Skillz.Commands;

internal sealed class RemoveCommandPrompter(IInteractionService interaction) : IRemoveCommandPrompter
{
    public async Task<ImmutableArray<string>> SelectSkillsAsync(
        ImmutableArray<string> installed,
        CancellationToken cancellationToken)
    {
        if (installed.Length == 0)
        {
            return [];
        }

        var choices = installed.Select(s => (s, s));
        return await interaction
            .MultiSelectAsync("Select skills to remove", choices, cancellationToken);
    }

    public Task<bool> ConfirmRemovalAsync(ImmutableArray<string> skills, CancellationToken cancellationToken)
    {
        var skillNames = string.Join(", ", skills);
        var message = $"Are you sure you want to remove {skills.Length} skill(s) [{skillNames}]?";
        return interaction.ConfirmAsync(message, defaultValue: false, cancellationToken);
    }
}
