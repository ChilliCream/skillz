using CookieCrumble;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Install;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class AddCommandSnapshotTests : IDisposable
{
    // A fixed, non-volatile source url so the rendered "Source:" line is a stable literal on every
    // platform (no cwd/home scrubbing involved, which differs across OSes that resolve symlinks).
    private const string LocalDisplaySource = "/skillz-test/local";

    // Fixed, short install paths kept well under the panel width so the Spectre summary box cannot
    // wrap nondeterministically and needs no scrubbing — identical box geometry on every machine.
    private const string CanonicalPath = "/skillz-test/.agents/skills/alpha";
    private const string InstallPath = "/skillz-test/.claude/skills/alpha";

    private readonly string _workspace;
    private readonly string _originalCwd;

    public AddCommandSnapshotTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-add-snap-" + Guid.NewGuid().ToString("N"));
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
        // The skill's own Path never reaches these snapshots; keep it a fixed constant for stability.
        return new Skill(
            Name: name,
            Description: description,
            Path: $"/skillz-test/source/{name}",
            RawContent: $"---\nname: {name}\ndescription: {description}\n---\n");
    }

    // The LocalProvider verifies the directory exists, so LocalPath must be a real existing directory;
    // only the displayed Url (first arg) is the fixed constant.
    private ParsedSource.Local LocalSource() => new(LocalDisplaySource, _workspace);

    private IServiceProvider BuildServices(
        Action<TestSourceParser>? configureParser = null,
        Action<TestSkillDiscovery>? configureDiscovery = null,
        Action<TestInstaller>? configureInstaller = null)
    {
        var services = CliTestHelper.CreateServiceProvider();

        configureParser?.Invoke(services.GetRequiredService<TestSourceParser>());
        configureDiscovery?.Invoke(services.GetRequiredService<TestSkillDiscovery>());
        configureInstaller?.Invoke(services.GetRequiredService<TestInstaller>());

        return services;
    }

    [Fact]
    public async Task Add_Without_Source()
    {
        var services = BuildServices();

        var output = await CommandSnapshot.RunAsync(services, "add");

        output.MatchInlineSnapshot(
            """
            $ skillz add
            # exit 1

            Missing required argument: source
            Usage: skillz add <source> [options]
            """);
    }

    [Fact]
    public async Task Add_With_No_Skills_Discovered()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => LocalSource(),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => Array.Empty<Skill>());

        var output = await CommandSnapshot.RunAsync(services, "add", "./local-path", "--yes", "--agent", "claude-code");

        output.MatchInlineSnapshot(
            """
            $ skillz add ./local-path --yes --agent claude-code
            # exit 1

            Source: /skillz-test/local
            No valid skills found. Skills require a SKILL.md with name and description.
            """);
    }

    [Fact]
    public async Task Add_With_Invalid_Agent()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => LocalSource(),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") });

        var output = await CommandSnapshot.RunAsync(services, "add", "./local-path", "--yes", "--agent", "bogus");

        output.MatchInlineSnapshot(
            """
            $ skillz add ./local-path --yes --agent bogus
            # exit 1

            Source: /skillz-test/local
            Found 1 skill(s)
            Invalid agents: bogus
            No agents selected.
            """);
    }

    [Fact]
    public async Task Add_List_Flag_Lists_Skills()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => LocalSource(),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[]
            {
                CreateSkill("alpha", "the alpha skill"),
                CreateSkill("beta", "the beta skill")
            });

        var output = await CommandSnapshot.RunAsync(services, "add", "./local-path", "--list");

        output.MatchInlineSnapshot(
            """
            $ skillz add ./local-path --list

            Source: /skillz-test/local
            Found 2 skill(s)

            Available Skills
              alpha
                the alpha skill
              beta
                the beta skill

            Use --skill <name> to install specific skills
            """);
    }

    [Fact]
    public async Task Add_With_Skill_Filter_No_Match()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => LocalSource(),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha"), CreateSkill("beta") });

        var output = await CommandSnapshot.RunAsync(
            services,
            "add",
            "./local-path",
            "--yes",
            "--agent",
            "claude-code",
            "--skill",
            "nope");

        output.MatchInlineSnapshot(
            """
            $ skillz add ./local-path --yes --agent claude-code --skill nope
            # exit 1

            Source: /skillz-test/local
            Found 2 skill(s)
            No matching skills found for: nope
            Available skills:
              alpha
              beta
            """);
    }

    [Fact]
    public async Task Add_Yes_Mode_Installs_Skill_Shows_Summary()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => LocalSource(),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
            {
                i.OnGetCanonicalPath = (_, _, _) => CanonicalPath;
                i.OnInstallRemoteSkill = (_, _, _) => new InstallResult(true, InstallPath);
            });

        var output = await CommandSnapshot.RunAsync(services, "add", "./local-path", "--yes", "--agent", "claude-code");

        output.MatchInlineSnapshot(
            """
            $ skillz add ./local-path --yes --agent claude-code

            Source: /skillz-test/local
            Found 1 skill(s)

            ┌─Installation Summary─────────────────────────────────────────────────────────┐
            │ Canonical: /skillz-test/.agents/skills/alpha                                 │
            │ Symlinked:  Claude Code                                                      │
            └──────────────────────────────────────────────────────────────────────────────┘
            ┌─Installed 1 skill(s)─────────────────────────────────────────────────────────┐
            │ ✓ alpha                                                                      │
            │   → /skillz-test/.claude/skills/alpha                                        │
            └──────────────────────────────────────────────────────────────────────────────┘

            Done!  Review skills before use; they run with full agent permissions.
            """);
    }

    [Fact]
    public async Task Add_Install_Failure_Shows_Failure_Panel()
    {
        var services = BuildServices(
            configureParser: p => p.OnParse = _ => LocalSource(),
            configureDiscovery: d => d.OnDiscover = (_, _, _) => new[] { CreateSkill("alpha") },
            configureInstaller: i =>
            {
                i.OnGetCanonicalPath = (_, _, _) => CanonicalPath;
                i.OnInstallRemoteSkill = (_, _, _) => new InstallResult(false, string.Empty, Error: "disk is full");
            });

        var output = await CommandSnapshot.RunAsync(services, "add", "./local-path", "--yes", "--agent", "claude-code");

        output.MatchInlineSnapshot(
            """
            $ skillz add ./local-path --yes --agent claude-code
            # exit 1

            Source: /skillz-test/local
            Found 1 skill(s)

            ┌─Installation failed──────────────────────────────────────────────────────────┐
            │ alpha → claude-code: disk is full                                            │
            └──────────────────────────────────────────────────────────────────────────────┘
            """);
    }
}
