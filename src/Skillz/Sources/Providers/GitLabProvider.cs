using System.Collections.Immutable;
using Skillz.Git;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class GitLabProvider(IGitClient gitClient, ISkillDiscovery skillDiscovery) : IProvider
{
    public string Id => "gitlab";

    public bool CanHandle(SkillSource source) => source is SkillSource.GitLab;

    public async Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken)
    {
        if (source is not SkillSource.GitLab gitlab)
        {
            throw new ArgumentException($"GitLabProvider cannot handle {source.GetType().Name}.", nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await gitClient.CloneAsync(gitlab.Url, tempDir, gitlab.Ref, cancellationToken);

            var discoveryOpts = new SkillDiscoveryOptions(
                options?.IncludeInternal ?? false,
                options?.FullDepth ?? false);

            var skills = await skillDiscovery.DiscoverAsync(tempDir, gitlab.Subpath, discoveryOpts, cancellationToken);

            return skills.ToRemoteSkills(Id, gitlab.Url, tempDir, cleanupPath: tempDir);
        }
        catch
        {
            TempDirCleanup.SafeDelete(tempDir);
            throw;
        }
    }
}
