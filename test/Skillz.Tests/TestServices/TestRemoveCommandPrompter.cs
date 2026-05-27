using Skillz.Commands;

namespace Skillz.Tests.TestServices;

internal sealed class TestRemoveCommandPrompter : IRemoveCommandPrompter
{
    public Func<IReadOnlyList<string>, IReadOnlyList<string>>? OnSelectSkills { get; set; }

    public Func<IReadOnlyList<string>, bool>? OnConfirmRemoval { get; set; }

    public Task<IReadOnlyList<string>> SelectSkillsAsync(
        IReadOnlyList<string> installed,
        CancellationToken cancellationToken = default)
    {
        var result = OnSelectSkills is not null ? OnSelectSkills(installed) : installed;
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmRemovalAsync(
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default)
    {
        var result = OnConfirmRemoval is null || OnConfirmRemoval(skills);
        return Task.FromResult(result);
    }
}
