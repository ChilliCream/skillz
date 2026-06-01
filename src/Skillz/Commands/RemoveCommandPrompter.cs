using Skillz.Interaction;

namespace Skillz.Commands;

internal sealed class RemoveCommandPrompter : IRemoveCommandPrompter
{
    private readonly IInteractionService _interaction;

    public RemoveCommandPrompter(IInteractionService interaction)
    {
        _interaction = interaction;
    }

    public async Task<IReadOnlyList<string>> SelectSkillsAsync(
        IReadOnlyList<string> installed,
        CancellationToken cancellationToken = default)
    {
        if (installed.Count == 0)
        {
            return [];
        }

        var choices = installed.Select(s => (s, s));
        return await _interaction
            .MultiSelectAsync("Select skills to remove", choices, cancellationToken);
    }

    public Task<bool> ConfirmRemovalAsync(IReadOnlyList<string> skills, CancellationToken cancellationToken = default)
    {
        var skillNames = string.Join(", ", skills);
        var message = $"Are you sure you want to remove {skills.Count} skill(s) [{skillNames}]?";
        return _interaction.ConfirmAsync(message, defaultValue: false, cancellationToken);
    }
}
