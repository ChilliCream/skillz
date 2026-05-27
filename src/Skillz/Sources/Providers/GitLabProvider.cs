using Skillz.Git;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class GitLabProvider : IProvider
{
    private readonly IGitClient _gitClient;
    private readonly ISkillDiscovery _skillDiscovery;

    public GitLabProvider(IGitClient gitClient, ISkillDiscovery skillDiscovery)
    {
        _gitClient = gitClient;
        _skillDiscovery = skillDiscovery;
    }

    public string Id => "gitlab";

    public bool CanHandle(ParsedSource source) => source is ParsedSource.GitLab;

    public async Task<IReadOnlyList<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        ProviderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is not ParsedSource.GitLab gitlab)
        {
            throw new ArgumentException(
                $"GitLabProvider cannot handle {source.GetType().Name}.",
                nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _gitClient.CloneAsync(gitlab.Url, tempDir, gitlab.Ref, cancellationToken)
                .ConfigureAwait(false);

            var includeInternal = options?.IncludeInternal ?? false;
            var discoveryOpts = new SkillDiscoveryOptions(
                IncludeInternal: includeInternal,
                FullDepth: options?.FullDepth ?? false);

            var skills = await _skillDiscovery
                .DiscoverAsync(tempDir, gitlab.Subpath, discoveryOpts, cancellationToken)
                .ConfigureAwait(false);

            return ProviderConversions.ToRemoteSkills(skills, Id, gitlab.Url, tempDir, cleanupPath: tempDir);
        }
        catch
        {
            TempDirCleanup.SafeDelete(tempDir);
            throw;
        }
    }
}
