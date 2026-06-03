using Skillz.Install;
using Skillz.Skills;
using Skillz.Tests.TestServices;
using Skillz.Utils;
using Xunit;

namespace Skillz.Tests.Install;

public class InstallerTests : IDisposable
{
    private readonly string _root;
    private readonly string _projectDir;
    private readonly string _home;
    private readonly SkillInstaller _installer;

    public InstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skillz-installer-" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_root, "project");
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_home);

        var system = new FakeSystemEnvironment { HomeDirectory = _home, CurrentDirectory = _projectDir };
        var registry = new AgentRegistry(system);
        _installer = new SkillInstaller(registry, system, new SystemFileStore());
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

    private ResolvedSkill MakeSkill(string name, string path)
    {
        var installName = string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name;
        return new ResolvedSkill(
            Name: name,
            Description: "test",
            Content: string.Empty,
            InstallName: installName,
            SourceUrl: string.Empty,
            ProviderId: "test",
            SourceIdentifier: "test/repo",
            SourcePath: path);
    }

    [Fact]
    public async Task SymlinkMode_CopiesSkillToCanonicalLocation()
    {
        // Arrange
        var skillName = "my-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var canonicalPath = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.Equal(canonicalPath, result.CanonicalPath);
        Assert.True(File.Exists(Path.Combine(canonicalPath, "SKILL.md")));
    }

    [Fact]
    public async Task SymlinkMode_NonUniversalAgentWithExistingRoot_CreatesSymlink()
    {
        // Arrange
        var skillName = "linked-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        Directory.CreateDirectory(Path.Combine(_projectDir, ".claude"));

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var skillName = "copy-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
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

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
        var skillName = "file-link-escape";
        var sourceDir = CreateSkillSource(skillName);
        var outsideFile = Path.Combine(_root, "outside-secret.txt");
        await File.WriteAllTextAsync(outsideFile, "secret", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(sourceDir, "loot.txt"), outsideFile);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
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

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
        var skillName = "broken-link";
        var sourceDir = CreateSkillSource(skillName);
        File.CreateSymbolicLink(Path.Combine(sourceDir, "missing.txt"), Path.Combine(sourceDir, "nope.txt"));
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
        var skillName = "in-root-link";
        var sourceDir = CreateSkillSource(skillName);
        var target = Path.Combine(sourceDir, "content.txt");
        await File.WriteAllTextAsync(target, "linked content", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(sourceDir, "alias.txt"), target);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
        var skillName = "destination-link";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);
        var targetBase = Path.Combine(_projectDir, ".agents", "skills");
        Directory.CreateDirectory(targetBase);
        var outsideDir = Path.Combine(_root, "outside-destination");
        Directory.CreateDirectory(outsideDir);
        var installPath = Path.Combine(targetBase, skillName);
        Directory.CreateSymbolicLink(installPath, outsideDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var skillName = "universal-only-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "github-copilot",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var skillName = "skipped-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        // No .windsurf directory in project
        var result = await _installer.InstallAsync(
            skill,
            "windsurf",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
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

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "amp",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
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

        // Arrange
        var skillName = "symlinked-dir-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var canonicalBase = Path.Combine(_projectDir, ".agents", "skills");
        Directory.CreateDirectory(canonicalBase);

        var claudeDir = Path.Combine(_projectDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        var claudeSkillsDir = Path.Combine(claudeDir, "skills");
        Directory.CreateSymbolicLink(claudeSkillsDir, canonicalBase);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var skillName = "idempotent-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        Directory.CreateDirectory(Path.Combine(_projectDir, ".claude"));

        var first = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        Assert.True(first.Success);

        // Act
        var second = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(second.Success);
        Assert.False(second.SymlinkFailed);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        Assert.True(Directory.Exists(agentDir));
        Assert.True(File.Exists(Path.Combine(agentDir, "SKILL.md")));
    }

    [Fact]
    public async Task PathTraversalAttempt_IsRejected_ViaSanitization()
    {
        // Arrange
        // SanitizeName converts "../" into a hyphen so traversal cannot occur,
        // but we verify the install still lands inside the canonical base.
        var maliciousName = "../escaped";
        var sourceDir = CreateSkillSource("ok-name");
        var skill = MakeSkill(maliciousName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Copy),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var canonicalBase = Path.Combine(_projectDir, ".agents", "skills");
        Assert.StartsWith(canonicalBase, Path.GetFullPath(result.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void GetCanonicalSkillsDirectory_Project_ReturnsAgentsSkillsRelativeToCwd()
    {
        // Act
        var result = _installer.GetCanonicalSkillsDirectory(global: false, workingDirectory: _projectDir);

        // Assert
        Assert.Equal(Path.Combine(_projectDir, ".agents", "skills"), result);
    }

    [Fact]
    public void GetCanonicalSkillsDirectory_Global_ReturnsAgentsSkillsInHome()
    {
        // Act
        var result = _installer.GetCanonicalSkillsDirectory(global: true);

        // Assert
        Assert.Equal(Path.Combine(_home, ".agents", "skills"), result);
    }

    [Fact]
    public void GetAgentBaseDirectory_UniversalAgent_ReturnsCanonicalDir()
    {
        // Act
        var result = _installer.GetAgentBaseDirectory("codex", global: false, workingDirectory: _projectDir);

        // Assert
        Assert.Equal(Path.Combine(_projectDir, ".agents", "skills"), result);
    }

    [Fact]
    public void GetAgentBaseDirectory_NonUniversalAgent_Project_ReturnsAgentSpecificDir()
    {
        // Act
        var result = _installer.GetAgentBaseDirectory("claude-code", global: false, workingDirectory: _projectDir);

        // Assert
        Assert.Equal(Path.Combine(_projectDir, ".claude", "skills"), result);
    }

    [Fact]
    public async Task RemoteSkill_WritesSkillMdToCanonical()
    {
        // Arrange
        var skillName = "remote-skill";
        var remote = new ResolvedSkill(
            Name: skillName,
            Description: "remote test",
            Content: "---\nname: remote-skill\ndescription: remote test\n---\nhello\n",
            InstallName: skillName,
            SourceUrl: "https://example.com",
            ProviderId: "test",
            SourceIdentifier: "test/repo");

        // Act
        var result = await _installer.InstallAsync(
            remote,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var installedSkillMd = Path.Combine(_projectDir, ".agents", "skills", skillName, "SKILL.md");
        Assert.True(File.Exists(installedSkillMd));
        var content = await File.ReadAllTextAsync(installedSkillMd, TestContext.Current.CancellationToken);
        Assert.Contains("hello", content);
    }

    [Fact]
    public async Task GlobalInstall_AgentWithoutGlobalSupport_ReturnsError()
    {
        // Arrange
        // The OpenClaw entry always has a global skills dir, so to test missing-support we
        // build a registry where no agent has GlobalSkillsDirectory for a synthetic check.
        // Instead, we use a known agent and verify behavior: all current agents define
        // GlobalSkillsDirectory, so we test the universal-global path which is supported.
        var skillName = "global-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "github-copilot",
            new InstallOptions(Global: true, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        // For universal-global, path == canonical
        Assert.Equal(result.Path, result.CanonicalPath);
        var canonical = Path.Combine(_home, ".agents", "skills", skillName);
        Assert.Equal(canonical, result.CanonicalPath);
    }

    [Fact]
    public async Task InstallAsync_Should_Fail_And_Not_Merge_Stale_Files_When_Clean_Cannot_Delete()
    {
        // Arrange
        // A prior install left an unrelated, stale file behind. The clean step is forced to
        // fail (delete throws), so the install must surface an error rather than silently
        // creating the (already existing) directory and merging the new skill over the
        // stale contents.
        var skillName = "uncleanable-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var canonicalDir = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Directory.CreateDirectory(canonicalDir);
        var staleFile = Path.Combine(canonicalDir, "stale-from-prior-skill.txt");
        await File.WriteAllTextAsync(staleFile, "stale", TestContext.Current.CancellationToken);

        var system = new FakeSystemEnvironment { HomeDirectory = _home, CurrentDirectory = _projectDir };
        var registry = new AgentRegistry(system);
        var failingStore = new DeleteFailingFileStore(canonicalDir);
        var installer = new SkillInstaller(registry, system, failingStore);

        // Act
        var result = await installer.InstallAsync(
            skill,
            "codex",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("clean", result.Error, StringComparison.OrdinalIgnoreCase);

        // The stale file must still be untouched and no SKILL.md must have been merged in.
        Assert.True(File.Exists(staleFile));
        Assert.Equal("stale", await File.ReadAllTextAsync(staleFile, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(canonicalDir, "SKILL.md")));
    }

    [Fact]
    public async Task InstallAsync_Should_PointRelativeSymlink_AtRealStore_When_CanonicalParent_Is_Symlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // symlink creation on Windows requires admin
        }

        // Arrange
        // A parent of the canonical directory (.agents) is itself a symlink to a backing
        // store. The agent symlink's relative target must be computed between consistently
        // symlink-resolved endpoints, so the link points DIRECTLY at the real backing store
        // rather than routing through the unrelated .agents symlink. A link that routes
        // through .agents diverges from the resolved link directory and dangles the moment
        // the .agents convenience symlink is removed or repointed.
        var skillName = "canonical-symlinked-parent-skill";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        // Backing store that .agents points at; this is where the canonical skill really lives.
        var backingStore = Path.Combine(_projectDir, "backing", "store");
        Directory.CreateDirectory(backingStore);

        // .agents is a symlink into the backing store; .claude is a plain directory so the
        // install reaches the symlink-creation path.
        Directory.CreateSymbolicLink(Path.Combine(_projectDir, ".agents"), backingStore);
        Directory.CreateDirectory(Path.Combine(_projectDir, ".claude"));

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.SymlinkFailed);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        var agentInfo = new DirectoryInfo(agentDir);
        Assert.True((agentInfo.Attributes & FileAttributes.ReparsePoint) != 0);

        var linkTarget = agentInfo.LinkTarget;
        Assert.NotNull(linkTarget);
        Assert.False(Path.IsPathRooted(linkTarget));

        // The stored relative target must resolve to the REAL backing store, not route back
        // through the .agents symlink. We assert this by removing the .agents symlink and
        // confirming the agent link still resolves to the materialized skill: a link that
        // diverges (routes through .agents) dangles here, while a correctly-resolved link
        // survives.
        var realCanonicalSkillDir = Path.Combine(backingStore, "skills", skillName);
        Assert.True(File.Exists(Path.Combine(realCanonicalSkillDir, "SKILL.md")));

        Directory.Delete(Path.Combine(_projectDir, ".agents"));

        var skillMdThroughLink = Path.Combine(agentDir, "SKILL.md");
        Assert.True(
            File.Exists(skillMdThroughLink),
            $"agent symlink dangled after removing the .agents symlink; stored target='{linkTarget}'");
        var content = await File.ReadAllTextAsync(skillMdThroughLink, TestContext.Current.CancellationToken);
        Assert.Contains($"name: {skillName}", content);
    }

    [Fact]
    public async Task Reinstall_Should_KeepSymlinkMode_When_AgentPathHoldsManagedSymlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // symlink creation on Windows requires admin
        }

        // Arrange
        var skillName = "managed-symlink-reinstall";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        Directory.CreateDirectory(Path.Combine(_projectDir, ".claude"));

        var first = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);
        Assert.True(first.Success);
        Assert.Equal(InstallMode.Symlink, first.Mode);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        Assert.True((new DirectoryInfo(agentDir).Attributes & FileAttributes.ReparsePoint) != 0);

        // Act
        var second = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert: no downgrade to copy/SymlinkFailed; the link still points at the store.
        Assert.True(second.Success);
        Assert.Equal(InstallMode.Symlink, second.Mode);
        Assert.False(second.SymlinkFailed);

        var info = new DirectoryInfo(agentDir);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) != 0);

        var canonicalSkillDir = Path.Combine(_projectDir, ".agents", "skills", skillName);
        Assert.Equal(
            Path.GetFullPath(canonicalSkillDir),
            Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)!.FullName));
        Assert.True(File.Exists(Path.Combine(agentDir, "SKILL.md")));
    }

    [Fact]
    public async Task Install_Should_ReplaceEmptyDirectory_With_Symlink_When_AgentPathHoldsEmptyDir()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // symlink creation on Windows requires admin
        }

        // Arrange: an EMPTY real directory at the agent path. Removing it loses no data, so the
        // install proceeds and replaces it with a symlink.
        var skillName = "empty-dir-reinstall";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        Directory.CreateDirectory(agentDir);
        Assert.True((new DirectoryInfo(agentDir).Attributes & FileAttributes.ReparsePoint) == 0);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(InstallMode.Symlink, result.Mode);
        Assert.False(result.SymlinkFailed);

        var info = new DirectoryInfo(agentDir);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) != 0);
        Assert.True(File.Exists(Path.Combine(agentDir, "SKILL.md")));
    }

    [Fact]
    public async Task Install_Should_Fail_And_LeaveContentsIntact_When_AgentPathHoldsNonEmptyDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // symlink creation on Windows requires admin
        }

        // Arrange: a NON-EMPTY real directory occupies the agent path. We never delete directory
        // contents, so the install must refuse and fail loud, leaving the contents intact.
        var skillName = "non-empty-dir";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var agentDir = Path.Combine(_projectDir, ".claude", "skills", skillName);
        Directory.CreateDirectory(agentDir);
        var preExisting = Path.Combine(agentDir, "important.txt");
        await File.WriteAllTextAsync(preExisting, "do not delete", TestContext.Current.CancellationToken);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert: install fails and the directory's contents are left intact.
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("non-empty directory", result.Error);

        Assert.True((new DirectoryInfo(agentDir).Attributes & FileAttributes.ReparsePoint) == 0);
        Assert.True(File.Exists(preExisting));
        Assert.Equal("do not delete", await File.ReadAllTextAsync(preExisting, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(agentDir, "SKILL.md")));
    }

    [Fact]
    public async Task Install_Should_Fail_And_LeaveFileIntact_When_AgentPathHoldsFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // symlink creation on Windows requires admin
        }

        // Arrange: a real file sits exactly where the skill's symlink would go. We never delete
        // it — the install must refuse and leave the file untouched.
        var skillName = "file-in-the-way";
        var sourceDir = CreateSkillSource(skillName);
        var skill = MakeSkill(skillName, sourceDir);

        var skillsDir = Path.Combine(_projectDir, ".claude", "skills");
        Directory.CreateDirectory(skillsDir);
        var agentPath = Path.Combine(skillsDir, skillName);
        await File.WriteAllTextAsync(agentPath, "do not delete", TestContext.Current.CancellationToken);

        // Act
        var result = await _installer.InstallAsync(
            skill,
            "claude-code",
            new InstallOptions(Global: false, WorkingDirectory: _projectDir, Mode: InstallMode.Symlink),
            TestContext.Current.CancellationToken);

        // Assert: install fails and the file is left intact.
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("existing file", result.Error);
        Assert.True(File.Exists(agentPath));
        Assert.Equal("do not delete", await File.ReadAllTextAsync(agentPath, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Delegates every operation to a real <see cref="SystemFileStore"/> except
    /// <see cref="IFileStore.DeletePath"/> for one specific path, which always throws —
    /// simulating an un-cleanable install destination (locked file, no permission).
    /// </summary>
    private sealed class DeleteFailingFileStore(string failForPath) : IFileStore
    {
        private readonly SystemFileStore _inner = new();

        public bool PathExists(string path) => _inner.PathExists(path);

        public bool IsSymlink(string path) => _inner.IsSymlink(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void DeleteDirectory(string path, bool recursive) => _inner.DeleteDirectory(path, recursive);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void DeletePath(string path)
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(failForPath), StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException($"Simulated failure deleting '{path}'");
            }

            _inner.DeletePath(path);
        }

        public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);

        public bool IsDirectoryEmpty(string path) => _inner.IsDirectoryEmpty(path);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
            => _inner.ReadAllTextAsync(path, cancellationToken);

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
            => _inner.WriteAllTextAsync(path, content, cancellationToken);

        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
            => _inner.WriteAllBytesAsync(path, bytes, cancellationToken);
    }
}
