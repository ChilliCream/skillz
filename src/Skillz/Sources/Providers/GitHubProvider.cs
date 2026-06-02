using System.Collections.Immutable;
using Skillz.Git;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class GitHubProvider(IGitClient gitClient, ISkillDiscovery skillDiscovery) : IProvider
{
    public string Id => "github";

    public bool CanHandle(SkillSource source) => source is SkillSource.GitHub;

    public async Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
        SkillSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken)
    {
        if (source is not SkillSource.GitHub github)
        {
            throw new ArgumentException($"GitHubProvider cannot handle {source.GetType().Name}.", nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await gitClient.CloneAsync(github.Url, tempDir, github.Ref, cancellationToken);

            var includeInternal = !string.IsNullOrEmpty(github.SkillFilter) || (options?.IncludeInternal ?? false);
            var discoveryOpts = new SkillDiscoveryOptions(includeInternal, options?.FullDepth ?? false);

            var skills = await skillDiscovery.DiscoverAsync(tempDir, github.Subpath, discoveryOpts, cancellationToken);

            return skills.ToRemoteSkills(Id, github.Url, tempDir, cleanupPath: tempDir);
        }
        catch
        {
            TempDirCleanup.SafeDelete(tempDir);
            throw;
        }
    }
}
