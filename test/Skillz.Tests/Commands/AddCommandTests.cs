using Microsoft.Extensions.DependencyInjection;
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
        var services = CliTestHelper.CreateServiceProvider();
        var cmd = services.GetRequiredService<AddCommand>();

        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Add_With_No_Skills_Discovered_Returns_Failure()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => Array.Empty<Skill>());

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Add_Yes_Mode_Installs_All_Discovered_Skills()
    {
        var installed = new List<(string Skill, string Agent)>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[]
            {
                CreateSkill("alpha"),
                CreateSkill("beta")
            },
            configureInstaller: i => i.OnInstallRemoteSkill = (skill, agent, _) =>
            {
                installed.Add((skill.InstallName, agent));
                return new InstallResult(true, $"/installed/{skill.InstallName}");
            });

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains(installed, x => x.Skill == "alpha" && x.Agent == "claude-code");
        Assert.Contains(installed, x => x.Skill == "beta" && x.Agent == "claude-code");
    }

    [Fact]
    public async Task Add_With_Skill_Filter_Installs_Only_Matching_Skills()
    {
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[]
            {
                CreateSkill("alpha"),
                CreateSkill("beta"),
                CreateSkill("gamma")
            },
            configureInstaller: i => i.OnInstallRemoteSkill = (skill, _, _) =>
            {
                installed.Add(skill.InstallName);
                return new InstallResult(true, $"/installed/{skill.InstallName}");
            });

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code", "--skill", "beta"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("beta", installed);
        Assert.DoesNotContain("alpha", installed);
        Assert.DoesNotContain("gamma", installed);
    }

    [Fact]
    public async Task Add_Project_Install_Updates_Project_Lock()
    {
        var lockEntries = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i => i.OnInstallRemoteSkill = (skill, _, _) =>
                new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureProjectLock: l => l.OnAddEntry = (name, _, _) => lockEntries.Add(name));

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("alpha", lockEntries);
    }

    [Fact]
    public async Task Add_Global_Install_Updates_Global_Lock()
    {
        var lockEntries = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.GitHub("https://github.com/owner/repo.git"),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i => i.OnInstallRemoteSkill = (skill, _, _) =>
                new InstallResult(true, $"/installed/{skill.InstallName}"),
            configureGlobalLock: l => l.OnAddEntry = (name, _) => lockEntries.Add(name));

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["owner/repo", "--yes", "--agent", "claude-code", "--global"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("alpha", lockEntries);
    }

    [Fact]
    public async Task Add_With_List_Flag_Lists_Skills_Without_Installing()
    {
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[]
            {
                CreateSkill("alpha"),
                CreateSkill("beta")
            },
            configureInstaller: i => i.OnInstallRemoteSkill = (skill, _, _) =>
            {
                installed.Add(skill.InstallName);
                return new InstallResult(true, $"/installed/{skill.InstallName}");
            });

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--list"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

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
        var installed = new List<string>();

        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[]
            {
                CreateSkill("alpha"),
                CreateSkill("beta")
            },
            configurePrompter: p =>
            {
                p.OnSelectSkills = skills => skills.Where(s => s.InstallName == "beta").ToList();
                p.OnConfirmInstallation = (_, _) => true;
            },
            configureInstaller: i => i.OnInstallRemoteSkill = (skill, _, _) =>
            {
                installed.Add(skill.InstallName);
                return new InstallResult(true, $"/installed/{skill.InstallName}");
            });

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--agent", "claude-code"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("beta", installed);
        Assert.DoesNotContain("alpha", installed);
    }

    [Fact]
    public async Task Add_With_Invalid_Agent_Returns_Failure()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => new ParsedSource.Local(_workspace, _workspace),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });

        var cmd = services.GetRequiredService<AddCommand>();
        var parseResult = cmd.Parse(["./local-path", "--yes", "--agent", "not-a-real-agent"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
    }
}
