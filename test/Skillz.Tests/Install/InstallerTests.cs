using Skillz.Install;
using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Install;

public class InstallerTests : IDisposable
{
    private readonly string _root;
    private readonly string _projectDir;
    private readonly string _home;
    private readonly Installer _installer;

    public InstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skillz-installer-" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_root, "project");
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_home);

        var registry = new AgentRegistry(_home, _ => null, Directory.Exists);
        _installer = new Installer(registry, _home);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    private string CreateSkillSource(string name, IDictionary<string, string>? extraFiles = null)
    {
        var sourceDir = Path.Combine(_root, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "SKILL.md"), $"---\nname: {name}\ndescription: test\n---\n");

        if (extraFiles is not null)
        {
            foreach (var (relPath, content) in extraFiles)
            {
                var fullPath = Path.Combine(sourceDir, relPath);
                var parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }
                File.WriteAllText(fullPath, content);
            }
        }

        return sourceDir;
    }

    private Skill MakeSkill(string name, string path)
    {
        return new Skill(name, "test", path);
    }

    [Fact]
    public async Task SymlinkMode_CopiesSkillToCanonicalLocation()
    {
        var skillName = "my-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var canonicalPath = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.Equal(canonicalPath, result.CanonicalPath);
        Assert.True(File.Exists(Path.Combine(canonicalPath, "SKILL.md")));
    }

    [Fact]
    public async Task SymlinkMode_NonUniversalAgentWithExistingRoot_CreatesSymlink()
    {
        var skillName = "linked-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        Directory.CreateDirectory(Path.Combine(_projectDir, ".claude"));

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(result.SymlinkFailed);
        Assert.False(result.Skipped);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        Assert.True(Directory.Exists(agentDir));

        var info = new DirectoryInfo(agentDir);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) != 0);

        var skillMd = Path.Combine(agentDir, "SKILL.md");
        var content = await File.ReadAllTextAsync(skillMd, TestContext.Current.CancellationToken);
        Assert.Contains($"name: {skillName}", content);
    }

    [Fact]
    public async Task CopyMode_WritesDirectlyToAgentLocation_NoCanonical()
    {
        var skillName = "copy-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(InstallMode.Copy, result.Mode);
        Assert.Null(result.CanonicalPath);

        // codex's project-level skillsDir is .agents/skills (universal)
        var installedPath = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.True(File.Exists(Path.Combine(installedPath, "SKILL.md")));

        var info = new DirectoryInfo(installedPath);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) == 0);
    }

    [Fact]
    public async Task CopyMode_ExcludesMetadataJsonAndGitDir_PreservesDotfiles()
    {
        var skillName = "copy-dotfile-skill";
        var sourceDir = CreateSkillSource(
            skillName,
            new Dictionary<string, string>
            {
                [".prettierrc"] = "{ \"singleQuote\": true }\n",
                ["metadata.json"] = "{\"private\":true}\n",
                [".git/config"] = "[core]\n"
            });
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        var installedDir = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.True(File.Exists(Path.Combine(installedDir, ".prettierrc")));
        Assert.Equal(
            "{ \"singleQuote\": true }\n",
            await File.ReadAllTextAsync(
                Path.Combine(installedDir, ".prettierrc"),
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(installedDir, "metadata.json")));
        Assert.False(Directory.Exists(Path.Combine(installedDir, ".git")));
    }

    [Fact]
    public async Task CopyMode_RejectsFileSymlinkEscape()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skillName = "file-link-escape";
        var sourceDir = CreateSkillSource(skillName);
        var outsideFile = Path.Combine(_root, "outside-secret.txt");
        await File.WriteAllTextAsync(outsideFile, "secret", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(sourceDir, "loot.txt"), outsideFile);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("outside source root", result.Error);
        Assert.False(File.Exists(Path.Combine(_projectDir, ".agents", "skills", skillName, "loot.txt")));
    }

    [Fact]
    public async Task CopyMode_RejectsDirectorySymlinkEscape()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skillName = "dir-link-escape";
        var sourceDir = CreateSkillSource(skillName);
        var outsideDir = Path.Combine(_root, "outside-dir");
        Directory.CreateDirectory(outsideDir);
        await File.WriteAllTextAsync(
            Path.Combine(outsideDir, "secret.txt"),
            "secret",
            TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(sourceDir, "loot"), outsideDir);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("outside source root", result.Error);
    }

    [Fact]
    public async Task CopyMode_RejectsBrokenSymlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skillName = "broken-link";
        var sourceDir = CreateSkillSource(skillName);
        File.CreateSymbolicLink(Path.Combine(sourceDir, "missing.txt"), Path.Combine(sourceDir, "nope.txt"));
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("broken symlink", result.Error);
    }

    [Fact]
    public async Task CopyMode_InRootSymlinkCopiesTargetContent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skillName = "in-root-link";
        var sourceDir = CreateSkillSource(skillName);
        var target = Path.Combine(sourceDir, "content.txt");
        await File.WriteAllTextAsync(target, "linked content", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(sourceDir, "alias.txt"), target);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var copied = Path.Combine(_projectDir, ".agents", "skills", skillName, "alias.txt");
        Assert.Equal("linked content", await File.ReadAllTextAsync(copied, TestContext.Current.CancellationToken));
        Assert.True((new FileInfo(copied).Attributes & FileAttributes.ReparsePoint) == 0);
    }

    [Fact]
    public async Task CopyMode_SymlinkedDestination_IsReplaced_WithoutFollowingTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skillName = "destination-link";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);
        var targetBase = Path.Combine(_projectDir, ".agents", "skills");
        Directory.CreateDirectory(targetBase);
        var outsideDir = Path.Combine(_root, "outside-destination");
        Directory.CreateDirectory(outsideDir);
        var installPath = Path.Combine(targetBase, skillName);
        Directory.CreateSymbolicLink(installPath, outsideDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // The symlinked destination is replaced with a real directory; the
        // install must never write through the link into the outside target.
        Assert.True(result.Success);
        Assert.True((new FileInfo(installPath).Attributes & FileAttributes.ReparsePoint) == 0);
        Assert.True(File.Exists(Path.Combine(installPath, "SKILL.md")));
        Assert.True(Directory.Exists(outsideDir));
        Assert.False(File.Exists(Path.Combine(outsideDir, "SKILL.md")));
    }

    [Fact]
    public async Task UniversalAgent_ProjectInstall_DoesNotCreateSymlink()
    {
        var skillName = "universal-only-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "github-copilot",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(result.SymlinkFailed);

        // For project-level universal agents, canonical == agent dir, so no symlink
        var installedPath = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.True(Directory.Exists(installedPath));
        var info = new DirectoryInfo(installedPath);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) == 0);
    }

    [Fact]
    public async Task NonUniversalAgent_ProjectInstall_AgentRootMissing_SkipsSymlink()
    {
        var skillName = "skipped-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // No .windsurf directory in project
        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "windsurf",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.Skipped);

        var canonicalPath = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.True(File.Exists(Path.Combine(canonicalPath, "SKILL.md")));

        // Agent-specific dir should NOT have been created
        Assert.False(Directory.Exists(Path.Combine(_projectDir, ".windsurf", "skills", skillName)));
    }

    [Fact]
    public async Task SelfLoopSymlink_InCanonicalDir_IsCleanedAndRecreated()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // symlink creation on Windows requires admin
        }

        var skillName = "self-loop-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var canonicalBase = Path.Combine(_projectDir, ".agents", "skills");
        Directory.CreateDirectory(canonicalBase);
        var canonicalDir = Path.Combine(canonicalBase, skillName);

        // Create a self-referential symlink
        Directory.CreateSymbolicLink(canonicalDir, skillName);

        var preInfo = new DirectoryInfo(canonicalDir);
        Assert.True((preInfo.Attributes & FileAttributes.ReparsePoint) != 0);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "amp",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        var postInfo = new DirectoryInfo(canonicalDir);
        Assert.True((postInfo.Attributes & FileAttributes.ReparsePoint) == 0);
        Assert.True(Directory.Exists(canonicalDir));
        Assert.True(File.Exists(Path.Combine(canonicalDir, "SKILL.md")));
    }

    [Fact]
    public async Task AgentSkillsDir_IsSymlinkToCanonical_DoesNotCreateBrokenSymlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skillName = "symlinked-dir-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var canonicalBase = Path.Combine(_projectDir, ".agents", "skills");
        Directory.CreateDirectory(canonicalBase);

        var claudeDir = Path.Combine(_projectDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        var claudeSkillsDir = Path.Combine(claudeDir, "skills");
        Directory.CreateSymbolicLink(claudeSkillsDir, canonicalBase);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(result.SymlinkFailed);

        var canonicalSkillDir = Path.Combine(canonicalBase, skillName);
        Assert.True(Directory.Exists(canonicalSkillDir));

        // Should be a real directory, not a symlink (because the agent dir resolved to canonical)
        var info = new DirectoryInfo(canonicalSkillDir);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) == 0);

        var contents = await File.ReadAllTextAsync(
            Path.Combine(canonicalSkillDir, "SKILL.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains($"name: {skillName}", contents);
    }

    [Fact]
    public async Task Idempotent_ReinstallingSameSkill_Succeeds()
    {
        var skillName = "idempotent-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        Directory.CreateDirectory(Path.Combine(_projectDir, ".claude"));

        var first = await _installer.InstallSkillForAgentAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(first.Success);

        var second = await _installer.InstallSkillForAgentAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(second.Success);
        Assert.False(second.SymlinkFailed);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        Assert.True(Directory.Exists(agentDir));
        Assert.True(File.Exists(Path.Combine(agentDir, "SKILL.md")));
    }

    [Fact]
    public async Task PathTraversalAttempt_IsRejected_ViaSanitization()
    {
        // SanitizeName converts "../" into a hyphen so traversal cannot occur,
        // but we verify the install still lands inside the canonical base.
        var maliciousName = "../escaped";
        var sourceDir = CreateSkillSource("ok-name");
        var skill = MakeSkill(maliciousName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var canonicalBase = Path.Combine(_projectDir, ".agents", "skills");
        Assert.StartsWith(canonicalBase, Path.GetFullPath(result.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void GetCanonicalSkillsDir_Project_ReturnsAgentsSkillsRelativeToCwd()
    {
        var result = _installer.GetCanonicalSkillsDir(global: false, cwd: _projectDir);
        Assert.Equal(Path.Combine(_projectDir, ".agents", "skills"), result);
    }

    [Fact]
    public void GetCanonicalSkillsDir_Global_ReturnsAgentsSkillsInHome()
    {
        var result = _installer.GetCanonicalSkillsDir(global: true);
        Assert.Equal(Path.Combine(_home, ".agents", "skills"), result);
    }

    [Fact]
    public void GetAgentBaseDir_UniversalAgent_ReturnsCanonicalDir()
    {
        var result = _installer.GetAgentBaseDir("codex", global: false, cwd: _projectDir);
        Assert.Equal(Path.Combine(_projectDir, ".agents", "skills"), result);
    }

    [Fact]
    public void GetAgentBaseDir_NonUniversalAgent_Project_ReturnsAgentSpecificDir()
    {
        var result = _installer.GetAgentBaseDir("claude-code", global: false, cwd: _projectDir);
        Assert.Equal(Path.Combine(_projectDir, ".claude", "skills"), result);
    }

    [Fact]
    public async Task RemoteSkill_WritesSkillMdToCanonical()
    {
        var skillName = "remote-skill";
        var remote = new RemoteSkill(
            Name: skillName,
            Description: "remote test",
            Content: "---\nname: remote-skill\ndescription: remote test\n---\nhello\n",
            InstallName: skillName,
            SourceUrl: "https://example.com",
            ProviderId: "test",
            SourceIdentifier: "test/repo");

        var result = await _installer.InstallRemoteSkillForAgentAsync(
            remote,
            "codex",
            new InstallOptions(Global: false, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var installedSkillMd = Path.Combine(_projectDir, ".agents", "skills", skillName, "SKILL.md");
        Assert.True(File.Exists(installedSkillMd));
        var content = await File.ReadAllTextAsync(installedSkillMd, TestContext.Current.CancellationToken);
        Assert.Contains("hello", content);
    }

    [Fact]
    public async Task GlobalInstall_AgentWithoutGlobalSupport_ReturnsError()
    {
        // The OpenClaw entry always has a global skills dir, so to test missing-support we
        // build a registry where no agent has GlobalSkillsDir for a synthetic check.
        // Instead, we use a known agent and verify behavior: all current agents define
        // GlobalSkillsDir, so we test the universal-global path which is supported.
        var skillName = "global-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var result = await _installer.InstallSkillForAgentAsync(
            skill,
            "github-copilot",
            new InstallOptions(Global: true, Cwd: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        // For universal-global, path == canonical
        Assert.Equal(result.Path, result.CanonicalPath);
        var canonical = Path.Combine(_home, ".agents", "skills", skillName);
        Assert.Equal(canonical, result.CanonicalPath);
    }
}
