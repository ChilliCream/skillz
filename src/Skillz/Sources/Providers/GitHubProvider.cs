using System.Collections.Immutable;
using Skillz.Git;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class GitHubProvider : IProvider
{
    private readonly IGitClient _gitClient;
    private readonly ISkillDiscovery _skillDiscovery;

    public GitHubProvider(IGitClient gitClient, ISkillDiscovery skillDiscovery)
    {
        _gitClient = gitClient;
        _skillDiscovery = skillDiscovery;
    }

    public string Id => "github";

    public bool CanHandle(ParsedSource source) => source is ParsedSource.GitHub;

    public async Task<ImmutableArray<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        ProviderOptions? options,
        CancellationToken cancellationToken)
    {
        if (source is not ParsedSource.GitHub github)
        {
            throw new ArgumentException($"GitHubProvider cannot handle {source.GetType().Name}.", nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _gitClient.CloneAsync(github.Url, tempDir, github.Ref, cancellationToken);

            var includeInternal = !string.IsNullOrEmpty(github.SkillFilter) || (options?.IncludeInternal ?? false);
            var discoveryOpts = new SkillDiscoveryOptions(
                IncludeInternal: includeInternal,
                FullDepth: options?.FullDepth ?? false);

            var skills = await _skillDiscovery
                .DiscoverAsync(tempDir, github.Subpath, discoveryOpts, cancellationToken);

            return ProviderConversions.ToRemoteSkills(skills, Id, github.Url, tempDir, cleanupPath: tempDir);
        }
        catch
        {
            TempDirCleanup.SafeDelete(tempDir);
            throw;
        }
    }
}
