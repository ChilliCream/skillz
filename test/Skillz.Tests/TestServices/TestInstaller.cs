using Skillz.Install;
using Skillz.Skills;

namespace Skillz.Tests.TestServices;

internal sealed class TestInstaller : IInstaller
{
    public Func<Skill, string, InstallOptions, InstallResult>? OnInstallSkill { get; set; }

    public Func<RemoteSkill, string, InstallOptions, InstallResult>? OnInstallRemoteSkill { get; set; }

    public Func<bool, string?, string>? OnGetCanonicalSkillsDir { get; set; }

    public Func<string, bool, string?, string>? OnGetAgentBaseDir { get; set; }

    public Func<string, bool, string?, string>? OnGetCanonicalPath { get; set; }

    public Func<string, string, bool, string?, string>? OnGetInstallPath { get; set; }

    public Task<InstallResult> InstallSkillForAgentAsync(
        Skill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = OnInstallSkill is not null
            ? OnInstallSkill(skill, agentType, options)
            : new InstallResult(true, skill.Path);
        return Task.FromResult(result);
    }

    public Task<InstallResult> InstallRemoteSkillForAgentAsync(
        RemoteSkill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = OnInstallRemoteSkill is not null
            ? OnInstallRemoteSkill(skill, agentType, options)
            : new InstallResult(true, skill.InstallName);
        return Task.FromResult(result);
    }

    public string GetCanonicalSkillsDir(bool global, string? cwd = null)
    {
        return OnGetCanonicalSkillsDir is not null ? OnGetCanonicalSkillsDir(global, cwd) : string.Empty;
    }

    public string GetAgentBaseDir(string agentType, bool global, string? cwd = null)
    {
        return OnGetAgentBaseDir is not null ? OnGetAgentBaseDir(agentType, global, cwd) : string.Empty;
    }

    public string GetCanonicalPath(string skillName, bool global = false, string? cwd = null)
    {
        return OnGetCanonicalPath is not null ? OnGetCanonicalPath(skillName, global, cwd) : string.Empty;
    }

    public string GetInstallPath(string skillName, string agentType, bool global = false, string? cwd = null)
    {
        return OnGetInstallPath is not null ? OnGetInstallPath(skillName, agentType, global, cwd) : string.Empty;
    }
}
