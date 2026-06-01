using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal interface IProvider
{
    string Id { get; }

    bool CanHandle(ParsedSource source);

    Task<ImmutableArray<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        ProviderOptions? options = null,
        CancellationToken cancellationToken = default);
}

internal sealed record ProviderOptions(bool FullDepth = false, bool IncludeInternal = false);
