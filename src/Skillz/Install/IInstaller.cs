using Skillz.Skills;

namespace Skillz.Install;

internal interface IInstaller
{
    Task<InstallResult> InstallSkillForAgentAsync(
        Skill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken);

    Task<InstallResult> InstallRemoteSkillForAgentAsync(
        RemoteSkill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken);

    string GetCanonicalSkillsDir(bool global, string? cwd = null);

    string GetAgentBaseDir(string agentType, bool global, string? cwd = null);

    string GetCanonicalPath(string skillName, bool global = false, string? cwd = null);

    string GetInstallPath(string skillName, string agentType, bool global = false, string? cwd = null);
}
