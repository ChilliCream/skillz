using System.Collections.Immutable;
using Skillz.Commands.Selection;
using Skillz.Skills;

namespace Skillz.Tests.TestServices;

internal sealed class TestSkillSelector : ISkillSelector
{
    public Func<IReadOnlyList<ResolvedSkill>, IReadOnlyList<ResolvedSkill>>? OnSelectSkills { get; set; }

    public Task<ImmutableArray<ResolvedSkill>> SelectAsync(
        ImmutableArray<ResolvedSkill> skills,
        CancellationToken cancellationToken)
    {
        var result = OnSelectSkills is not null ? OnSelectSkills(skills) : skills;
        return Task.FromResult<ImmutableArray<ResolvedSkill>>([.. result]);
    }
}
