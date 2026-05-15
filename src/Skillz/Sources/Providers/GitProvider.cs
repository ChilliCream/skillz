using Skillz.Git;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed class GitProvider : IProvider
{
    private readonly IGitClient _gitClient;
    private readonly ISkillDiscovery _skillDiscovery;

    public GitProvider(IGitClient gitClient, ISkillDiscovery skillDiscovery)
    {
        _gitClient = gitClient;
        _skillDiscovery = skillDiscovery;
    }

    public string Id => "git";

    public bool CanHandle(ParsedSource source) => source is ParsedSource.Git;

    public async Task<IReadOnlyList<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        CancellationToken cancellationToken = default)
    {
        if (source is not ParsedSource.Git git)
        {
            throw new ArgumentException(
                $"GitProvider cannot handle {source.GetType().Name}.",
                nameof(source));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _gitClient.CloneAsync(git.Url, tempDir, git.Ref, cancellationToken)
                .ConfigureAwait(false);

            var skills = await _skillDiscovery
                .DiscoverAsync(tempDir, subpath: null, options: null, cancellationToken)
                .ConfigureAwait(false);

            return ProviderConversions.ToRemoteSkills(skills, Id, git.Url, tempDir);
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
