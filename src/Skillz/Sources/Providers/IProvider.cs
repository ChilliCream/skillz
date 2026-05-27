using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal interface IProvider
{
    string Id { get; }

    bool CanHandle(ParsedSource source);

    Task<IReadOnlyList<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        ProviderOptions? options = null,
        CancellationToken cancellationToken = default);
}

internal sealed record ProviderOptions(bool FullDepth = false, bool IncludeInternal = false);
