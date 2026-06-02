using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal interface IProvider
{
    string Id { get; }

    bool CanHandle(SkillSource source);

    Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken);
}

internal sealed record ProviderOptions(bool FullDepth = false, bool IncludeInternal = false);
