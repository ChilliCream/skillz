using System.Collections.Immutable;
using Skillz.Skills;
using Skillz.Utils;

namespace Skillz.Sources.Providers;

internal sealed class LocalProvider(ISkillDiscovery skillDiscovery, IFileStore fileStore) : IProvider
{
    public string Id => "local";

    public bool CanHandle(SkillSource source) => source is SkillSource.Local;

    public async Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken)
    {
        if (source is not SkillSource.Local local)
        {
            throw new ArgumentException($"LocalProvider cannot handle {source.GetType().Name}.", nameof(source));
        }

        if (!fileStore.DirectoryExists(local.LocalPath))
        {
            throw new CliException(ExitCodeConstants.Failure, $"Local path does not exist: {local.LocalPath}");
        }

        var discoveryOptions = new SkillDiscoveryOptions(
            options?.IncludeInternal ?? false,
            options?.FullDepth ?? false);

        var skills = await skillDiscovery.DiscoverAsync(
            local.LocalPath,
            subpath: null,
            discoveryOptions,
            cancellationToken);

        return skills.ToRemoteSkills(Id, local.LocalPath);
    }
}
