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

    public async Task<IReadOnlyList<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        CancellationToken cancellationToken = default)
    {
        if (source is not ParsedSource.GitHub github)
        {
            throw new ArgumentException(
                $"GitHubProvider cannot handle {source.GetType().Name}.",
                nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _gitClient.CloneAsync(github.Url, tempDir, github.Ref, cancellationToken)
                .ConfigureAwait(false);

            var includeInternal = !string.IsNullOrEmpty(github.SkillFilter);
            var options = new SkillDiscoveryOptions(IncludeInternal: includeInternal);

            var skills = await _skillDiscovery
                .DiscoverAsync(tempDir, github.Subpath, options, cancellationToken)
                .ConfigureAwait(false);

            return ProviderConversions.ToRemoteSkills(skills, Id, github.Url, tempDir);
        }
        finally
        {
            SafeCleanup(tempDir);
        }
    }

    private static void SafeCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
        }
    }
}
