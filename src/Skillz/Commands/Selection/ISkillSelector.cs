using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Commands.Selection;

/// <summary>
/// Chooses which of the resolved skills to install, applying the grouping policy that mirrors
/// how <c>skillz list</c> renders: a flat picker when nothing is plugin-claimed, a grouped tree
/// otherwise. Injected so command flow can be tested without driving a TTY.
/// </summary>
internal interface ISkillSelector
{
    Task<ImmutableArray<ResolvedSkill>> SelectAsync(
        ImmutableArray<ResolvedSkill> skills,
        CancellationToken cancellationToken);
}
