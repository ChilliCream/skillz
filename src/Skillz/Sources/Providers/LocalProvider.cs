using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class LocalProvider : IProvider
{
    private readonly ISkillDiscovery _skillDiscovery;

    public LocalProvider(ISkillDiscovery skillDiscovery)
    {
        _skillDiscovery = skillDiscovery;
    }

    public string Id => "local";

    public bool CanHandle(ParsedSource source) => source is ParsedSource.Local;

    public async Task<ImmutableArray<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken)
    {
        if (source is not ParsedSource.Local local)
        {
            throw new ArgumentException($"LocalProvider cannot handle {source.GetType().Name}.", nameof(source));
        }

        if (!Directory.Exists(local.LocalPath))
        {
            throw new CliException(ExitCodeConstants.Failure, $"Local path does not exist: {local.LocalPath}");
        }

        var discoveryOptions = new SkillDiscoveryOptions(
            IncludeInternal: options?.IncludeInternal ?? false,
            FullDepth: options?.FullDepth ?? false);

        var skills = await _skillDiscovery
            .DiscoverAsync(local.LocalPath, subpath: null, discoveryOptions, cancellationToken);

        return ProviderConversions.ToRemoteSkills(skills, Id, local.LocalPath);
    }
}
