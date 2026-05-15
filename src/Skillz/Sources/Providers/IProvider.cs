using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal interface IProvider
{
    string Id { get; }

    bool CanHandle(ParsedSource source);

    Task<IReadOnlyList<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        CancellationToken cancellationToken = default);
}
