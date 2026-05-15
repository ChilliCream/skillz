using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal static class ProviderConversions
{
    public static IReadOnlyList<RemoteSkill> ToRemoteSkills(
        IReadOnlyList<Skill> skills,
        string providerId,
        string sourceIdentifier,
        string? cloneRoot = null)
    {
        if (skills.Count == 0)
        {
            return [];
        }

        var result = new List<RemoteSkill>(skills.Count);
        foreach (var skill in skills)
        {
            var content = skill.RawContent ?? string.Empty;
            string? skillPath = null;
            if (cloneRoot is not null)
            {
                var skillMd = Path.Combine(skill.Path, KnownConfigNames.SkillFileName);
                skillPath = Path.GetRelativePath(cloneRoot, skillMd).Replace('\\', '/');
            }

            result.Add(new RemoteSkill(
                Name: skill.Name,
                Description: skill.Description,
                Content: content,
                InstallName: skill.Name,
                SourceUrl: skill.Path,
                ProviderId: providerId,
                SourceIdentifier: sourceIdentifier,
                SkillPath: skillPath,
                Metadata: skill.Metadata));
        }

        return result;
    }
}
