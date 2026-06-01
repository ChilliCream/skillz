using System.Collections.Immutable;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal static class ProviderConversions
{
    public static ImmutableArray<RemoteSkill> ToRemoteSkills(
        ImmutableArray<Skill> skills,
        string providerId,
        string sourceIdentifier,
        string? cloneRoot = null,
        string? cleanupPath = null)
    {
        if (skills.Length == 0)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<RemoteSkill>(skills.Length);
        foreach (var skill in skills)
        {
            var content = skill.RawContent ?? string.Empty;
            string? skillPath = null;
            if (cloneRoot is not null)
            {
                var skillMd = Path.Combine(skill.Path, KnownConfigNames.SkillFileName);
                skillPath = Path.GetRelativePath(cloneRoot, skillMd).Replace('\\', '/');
            }

            result.Add(
                new RemoteSkill(
                    Name: skill.Name,
                    Description: skill.Description,
                    Content: content,
                    InstallName: skill.Name,
                    SourceUrl: skill.Path,
                    ProviderId: providerId,
                    SourceIdentifier: sourceIdentifier,
                    SkillPath: skillPath,
                    SourcePath: skill.Path,
                    Metadata: skill.Metadata,
                    CleanupPath: cleanupPath,
                    PluginName: skill.PluginName));
        }

        return result.ToImmutable();
    }
}
