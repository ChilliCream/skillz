using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

/// <summary>
/// Fetches skills from one kind of source (GitHub, GitLab, a local path, a well-known
/// HTTP endpoint, …). The registry routes each source to the first provider whose
/// <see cref="CanHandle"/> returns <see langword="true"/>.
/// </summary>
internal interface IProvider
{
    /// <summary>
    /// A stable identifier for this provider, unique across all registered providers
    /// (it is recorded in lock entries to attribute an installed skill to its source kind).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Returns <see langword="true"/> when this provider can fetch skills from
    /// <paramref name="source"/>. Exactly one provider is expected to claim a given source.
    /// </summary>
    bool CanHandle(SkillSource source);

    Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken);
}

internal sealed record ProviderOptions(bool FullDepth = false, bool IncludeInternal = false);
