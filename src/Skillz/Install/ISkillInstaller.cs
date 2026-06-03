using Skillz.Skills;

namespace Skillz.Install;

/// <summary>
/// Installs a skill into one canonical store and exposes it to individual agents.
/// A skill is materialized once under the canonical skills directory and then made
/// available to each agent's base directory, by default via a symlink back to the
/// canonical copy (or a full copy when symlinks are unavailable or not requested).
/// The path methods compute these locations without performing any filesystem changes.
/// </summary>
internal interface ISkillInstaller
{
    Task<InstallResult> InstallAsync(
        ResolvedSkill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken);

    string GetCanonicalSkillsDirectory(bool global, string? workingDirectory = null);

    string GetAgentBaseDirectory(string agentType, bool global, string? workingDirectory = null);

    string GetCanonicalPath(string skillName, bool global = false, string? workingDirectory = null);

    string GetInstallPath(string skillName, string agentType, bool global = false, string? workingDirectory = null);
}
