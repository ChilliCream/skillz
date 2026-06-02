using System.Collections.Immutable;
using Skillz.Git;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class GitProvider(IGitClient gitClient, ISkillDiscovery skillDiscovery) : IProvider
{
    public string Id => "git";

    public bool CanHandle(SkillSource source) => source is SkillSource.Git;

    public async Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken)
    {
        if (source is not SkillSource.Git git)
        {
            throw new ArgumentException($"GitProvider cannot handle {source.GetType().Name}.", nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await gitClient.CloneAsync(git.Url, tempDir, git.Ref, cancellationToken);

            var discoveryOpts = new SkillDiscoveryOptions(
                options?.IncludeInternal ?? false,
                options?.FullDepth ?? false);

            var skills = await skillDiscovery.DiscoverAsync(tempDir, subpath: null, discoveryOpts, cancellationToken);

            return skills.ToRemoteSkills(Id, git.Url, tempDir, cleanupPath: tempDir);
        }
        catch
        {
            TempDirCleanup.SafeDelete(tempDir);
            throw;
        }
    }
}
