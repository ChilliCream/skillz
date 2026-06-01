using Microsoft.Extensions.DependencyInjection;
using Skillz;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
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

    private static IServiceProvider BuildServices(
        Action<TestSourceParser>? configureParser = null,
        Action<TestSkillDiscovery>? configureDiscovery = null,
        Action<TestAddCommandPrompter>? configurePrompter = null,
        Action<TestAgentEnvironmentDetector>? configureDetector = null,
        Action<TestInstaller>? configureInstaller = null,
        Action<TestProjectLockFile>? configureProjectLock = null,
        Action<TestGlobalLockFile>? configureGlobalLock = null)
    {
        var services = CliTestHelper.CreateServiceProvider();

        configureParser?.Invoke(services.GetRequiredService<TestSourceParser>());
        configureDiscovery?.Invoke(services.GetRequiredService<TestSkillDiscovery>());
        configurePrompter?.Invoke(services.GetRequiredService<TestAddCommandPrompter>());
        configureDetector?.Invoke(services.GetRequiredService<TestAgentEnvironmentDetector>());
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
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
            configureParser: p => p.OnParse = _ => new ParsedSource.GitHub("https://github.com/owner/repo.git"),
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
    public async Task Add_With_List_Flag_Lists_Skills_Without_Installing()
    {
        // Arrange
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
        Directory.CreateDirectory(canonical);

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
        Directory.CreateDirectory(canonical);
        var installed = false;

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
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
        Directory.CreateDirectory(canonical);

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });
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
        var warningIndex = output.FindIndex(line => line.Contains("Overwriting existing skill", StringComparison.Ordinal));
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
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });

        // Act
        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "not-a-real-agent"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(0, exitCode);
    }
}
