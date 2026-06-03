using Microsoft.Extensions.DependencyInjection;
using Skillz;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class AddCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public AddCommandTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        _originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_workspace);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static Skill CreateSkill(string name, string description = "test skill")
    {
        return new Skill(
            Name: name,
            Description: description,
            Path: $"/tmp/{name}",
            RawContent: $"---\nname: {name}\ndescription: {description}\n---\n");
    }

    private IServiceProvider BuildServices(
        Action<TestSourceParser>? configureParser = null,
        Action<TestSkillDiscovery>? configureDiscovery = null,
        Action<TestAddCommandPrompter>? configurePrompter = null,
        Action<TestInstaller>? configureInstaller = null,
        Action<TestProjectLockFile>? configureProjectLock = null,
        Action<TestGlobalLockFile>? configureGlobalLock = null)
    {
        var services = CliTestHelper.CreateServiceProvider();

        // The real LocalProvider guards on the source directory existing; register the workspace
        // so local-source installs proceed to the (faked) discovery step.
        services.GetRequiredService<FakeFileStore>().CreateDirectory(_workspace);

        configureParser?.Invoke(services.GetRequiredService<TestSourceParser>());
        configureDiscovery?.Invoke(services.GetRequiredService<TestSkillDiscovery>());
        configurePrompter?.Invoke(services.GetRequiredService<TestAddCommandPrompter>());
        configureInstaller?.Invoke(services.GetRequiredService<TestInstaller>());
        configureProjectLock?.Invoke(services.GetRequiredService<TestProjectLockFile>());
        configureGlobalLock?.Invoke(services.GetRequiredService<TestGlobalLockFile>());

        return services;
    }

    [Fact]
    public async Task Add_Without_Source_Returns_Failure()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider();
        var cmd = services.GetRequiredService<AddCommand>();

        // Act
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Add_With_No_Skills_Discovered_Returns_Failure()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => Array.Empty<Skill>());

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Add_Yes_Mode_Installs_All_Discovered_Skills()
    {
        // Arrange
        var installed = new List<(string Skill, string Agent)>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha"), CreateSkill("beta") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, agent, _) =>
                {
                    installed.Add((skill.InstallName, agent));
                    return new InstallResult(true, $"/installed/{skill.InstallName}");
                });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(installed, x => x.Skill == "alpha" && x.Agent == "claude-code");
        Assert.Contains(installed, x => x.Skill == "beta" && x.Agent == "claude-code");
    }

    [Fact]
    public async Task Add_With_Skill_Filter_Installs_Only_Matching_Skills()
    {
        // Arrange
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d =>
                d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha"), CreateSkill("beta"), CreateSkill("gamma") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) =>
                {
                    installed.Add(skill.InstallName);
                    return new InstallResult(true, $"/installed/{skill.InstallName}");
                });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code", "--skill", "beta"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("beta", installed);
        Assert.DoesNotContain("alpha", installed);
        Assert.DoesNotContain("gamma", installed);
    }

    [Fact]
    public async Task Add_Project_Install_Updates_Project_Lock()
    {
        // Arrange
        var lockEntries = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureProjectLock: l => l.OnAddEntry = (name, _, _) => lockEntries.Add(name));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("alpha", lockEntries);
    }

    [Fact]
    public async Task Add_Global_Install_Updates_Global_Lock()
    {
        // Arrange
        var lockEntries = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.GitHub("https://github.com/owner/repo.git"),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureGlobalLock: l => l.OnAddEntry = (name, _) => lockEntries.Add(name));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["owner/repo", "--yes", "--agent", "claude-code", "--global"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("alpha", lockEntries);
    }

    [Fact]
    public async Task Add_Should_StripCredentialsFromPersistedSource_When_GitUrlHasMultiAtUserInfo()
    {
        // Arrange — a single-segment git URL (no owner/repo) persists its URL into the project
        // lock's Source. A password containing '@' must have its WHOLE userinfo stripped.
        LocalSkillLockEntry? captured = null;

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Git("https://user:p@ss@git.example.com/repo.git"),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureProjectLock: l => l.OnAddEntry = (_, entry, _) => captured = entry);

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal("https://git.example.com/repo.git", captured!.Source);
        Assert.DoesNotContain("p@ss", captured.Source);
        Assert.DoesNotContain("user", captured.Source);
    }

    [Fact]
    public async Task Add_Should_StripCredentialsFromPersistedSourceUrl_When_GlobalGitHubUrlHasUserInfo()
    {
        // Arrange — global install records the raw URL in SourceUrl, which must not leak credentials.
        SkillLockEntry? captured = null;

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.GitHub("https://token@github.com/owner/repo.git"),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureGlobalLock: l => l.OnAddEntry = (_, entry) => captured = entry);

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["owner/repo", "--yes", "--agent", "claude-code", "--global"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal("https://github.com/owner/repo.git", captured!.SourceUrl);
        Assert.DoesNotContain("token", captured.SourceUrl);
        Assert.DoesNotContain("@", captured.SourceUrl);
    }

    [Fact]
    public async Task Add_Should_RedactCredentialsInSourceLine_When_GitUrlHasUserInfo()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p =>
                p.OnParse = _ => new SkillSource.Git("https://user:s3cr3t@example.com/owner/repo.git"),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("s3cr3t", interaction.OutputText);
        Assert.Contains(
            "Source: https://<redacted>@example.com/owner/repo.git",
            interaction.OutputText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_With_List_Flag_Lists_Skills_Without_Installing()
    {
        // Arrange
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha"), CreateSkill("beta") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) =>
                {
                    installed.Add(skill.InstallName);
                    return new InstallResult(true, $"/installed/{skill.InstallName}");
                });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--list"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(installed);

        var interaction = (TestInteractionService)services.GetRequiredService<IInteractionService>();
        var output = string.Join("\n", interaction.Output);
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
    }

    [Fact]
    public async Task Add_Uses_Prompter_For_Skill_Selection_When_Interactive()
    {
        // Arrange
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha"), CreateSkill("beta") },
            configurePrompter: p =>
            {
                p.OnSelectSkills = skills => skills.Where(s => s.InstallName == "beta").ToList();
                p.OnConfirmInstallation = (_, _, _) => true;
            },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) =>
                {
                    installed.Add(skill.InstallName);
                    return new InstallResult(true, $"/installed/{skill.InstallName}");
                });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("beta", installed);
        Assert.DoesNotContain("alpha", installed);
    }

    [Fact]
    public async Task Add_Interactive_Confirmation_Includes_Existing_Canonical_Path()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".skillz", "skills", "alpha");

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configurePrompter: p =>
            {
                p.OnConfirmInstallation = (_, _, overwrites) =>
                    overwrites.Count == 1 && overwrites[0].DestinationPath == canonical;
            },
            configureInstaller: i =>
            {
                i.OnGetCanonicalPath = (skill, _, _) => Path.Combine(_workspace, ".skillz", "skills", skill);
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, canonical);
            });
        services.GetRequiredService<FakeFileStore>().CreateDirectory(canonical);

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var prompter = services.GetRequiredService<TestAddCommandPrompter>();
        Assert.Equal(0, exitCode);
        Assert.Single(prompter.LastOverwriteTargets);
        Assert.Equal(canonical, prompter.LastOverwriteTargets[0].DestinationPath);
    }

    [Fact]
    public async Task Add_Interactive_Cancel_With_Overwrite_Does_Not_Install()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".skillz", "skills", "alpha");
        var installed = false;

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configurePrompter: p => p.OnConfirmInstallation = (_, _, _) => false,
            configureInstaller: i =>
            {
                i.OnGetCanonicalPath = (skill, _, _) => Path.Combine(_workspace, ".skillz", "skills", skill);
                i.OnInstallRemoteSkill = (_, _, _) =>
                {
                    installed = true;
                    return new InstallResult(true, canonical);
                };
            });
        services.GetRequiredService<FakeFileStore>().CreateDirectory(canonical);

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ExitCodeConstants.Cancelled, exitCode);
        Assert.False(installed);
    }

    [Fact]
    public async Task Add_Yes_Warns_About_Overwrite_Before_Installing()
    {
        // Arrange
        var canonical = Path.Combine(_workspace, ".skillz", "skills", "alpha");

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });
        services.GetRequiredService<FakeFileStore>().CreateDirectory(canonical);
        var interaction = services.GetRequiredService<TestInteractionService>();
        var installer = services.GetRequiredService<TestInstaller>();
        installer.OnGetCanonicalPath = (skill, _, _) => Path.Combine(_workspace, ".skillz", "skills", skill);
        installer.OnInstallRemoteSkill = (_, _, _) =>
        {
            interaction.WriteLine("INSTALL ACTION");
            return new InstallResult(true, canonical);
        };

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var output = services.GetRequiredService<TestInteractionService>().Output.ToList();
        var warningIndex = output.FindIndex(line =>
            line.Contains("Overwriting existing skill", StringComparison.Ordinal)
        );
        var installIndex = output.FindIndex(line => line.Contains("INSTALL ACTION", StringComparison.Ordinal));
        Assert.Equal(0, exitCode);
        Assert.True(warningIndex >= 0);
        Assert.True(installIndex > warningIndex);
    }

    [Fact]
    public async Task Add_With_Invalid_Agent_Returns_Failure()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "not-a-real-agent"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_Should_ReportInvalidAgentOnce_When_AgentIsUnknown()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "bogus"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(ExitCodeConstants.Failure, exitCode);
        Assert.Contains(
            interaction.Output,
            line =>
                line.Contains("Invalid agents", StringComparison.Ordinal)
                && line.Contains("bogus", StringComparison.Ordinal));
        Assert.Contains(
            interaction.Output,
            line =>
                line.Contains("TIP:", StringComparison.Ordinal)
                && line.Contains("Valid agents", StringComparison.Ordinal));
        Assert.DoesNotContain(
            interaction.Output,
            line => line.Contains("No agents selected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Should_FailAndRecordOnlySuccesses_When_SomeInstallsFail()
    {
        // Arrange
        var lockEntries = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha"), CreateSkill("beta") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) =>
                    skill.InstallName == "beta"
                        ? new InstallResult(false, string.Empty, Error: "disk is full")
                        : new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureProjectLock: l => l.OnAddEntry = (name, _, _) => lockEntries.Add(name));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(ExitCodeConstants.Failure, exitCode);
        Assert.Contains("alpha", lockEntries);
        Assert.DoesNotContain("beta", lockEntries);
        Assert.Contains("Installed 1 skill(s)", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("Installation failed", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_FailWithoutLockWrites_When_AllInstallsFail()
    {
        // Arrange
        var lockEntries = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (_, _, _) => new InstallResult(false, string.Empty, Error: "boom"),
            configureProjectLock: l => l.OnAddEntry = (name, _, _) => lockEntries.Add(name));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(ExitCodeConstants.Failure, exitCode);
        Assert.Empty(lockEntries);
        Assert.Contains("Installation failed", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("Installed 1 skill(s)", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_RenderSymlinkedLabel_When_InstallingAcrossDistinctSkillDirs()
    {
        // Arrange: two non-universal agents with distinct skills dirs, non-interactive → symlink default.
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, agent, _) =>
                    new InstallResult(true, $"/installed/{agent}/{skill.InstallName}"));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code", "--agent", "augment"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(0, exitCode);
        Assert.Contains("Symlinked:", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("Copied:", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_RenderCopiedLabel_When_InstallingWithCopyMode()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse([
            "./local-path",
            "--yes",
            "--copy",
            "--agent",
            "claude-code",
            "--agent",
            "augment"
        ]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(0, exitCode);
        Assert.Contains("Copied:", interaction.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("Symlinked:", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_SucceedAndWarn_When_LockWriteThrows()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (skill, _, _) => new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureProjectLock: l => l.OnAddEntry = (_, _, _) => throw new IOException("lock file is read-only"));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(0, exitCode);
        Assert.Contains(
            interaction.Output,
            line =>
                line.Contains("Could not record lock entry", StringComparison.Ordinal)
                && line.Contains("lock file is read-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Should_RecordFailure_When_InstallerThrowsNonCancellation()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
                i.OnInstallRemoteSkill = (_, _, _) => throw new InvalidOperationException("installer exploded"));

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the throw becomes a failed entry, the command reports failure instead of unwinding.
        var interaction = services.GetRequiredService<TestInteractionService>();
        Assert.Equal(ExitCodeConstants.Failure, exitCode);
        Assert.Contains("Installation failed", interaction.OutputText, StringComparison.Ordinal);
        Assert.Contains("installer exploded", interaction.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Should_PropagateCancellation_When_InstallerThrowsOperationCanceled()
    {
        // Arrange
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new SkillSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i => i.OnInstallRemoteSkill = (_, _, _) => throw new OperationCanceledException());

        var executor = services.GetRequiredService<AddCommandExecutor>();
        var options = new AddCommandOptions(
            Source: "./local-path",
            Global: false,
            Agents: ["claude-code"],
            SkillFilters: [],
            Yes: true,
            All: false,
            Copy: false,
            FullDepth: false,
            List: false);

        // Act & Assert: cancellation is not swallowed by the per-skill failure aggregation.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.RunAsync(options, TestContext.Current.CancellationToken)
        );
    }
}
