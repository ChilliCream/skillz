using CookieCrumble;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class InitCommandSnapshotTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public InitCommandSnapshotTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-init-snap-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Init_With_Name_Creates_Skill()
    {
        var services = CliTestHelper.CreateServiceProvider(useRealFileStore: true);

        var output = await CommandSnapshot.RunAsync(services, "init", "my-skill");

        output.MatchInlineSnapshot(
            """
            $ skillz init my-skill

            Initialized skill: my-skill

            Created:
              my-skill/SKILL.md

            Next steps:
              1. Edit my-skill/SKILL.md to define your skill instructions
              2. Update the name and description in the frontmatter

            Publishing:
              GitHub: Push to a repo, then skillz add <owner>/<repo>
              URL:    Host the file, then skillz add https://example.com/my-skill/SKILL.md
            """);
    }

    [Fact]
    public async Task Init_When_Skill_Already_Exists_Warns()
    {
        var skillDir = Path.Combine(_workspace, "my-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "SKILL.md"),
            "existing content",
            TestContext.Current.CancellationToken);
        var services = CliTestHelper.CreateServiceProvider(useRealFileStore: true);

        var output = await CommandSnapshot.RunAsync(services, "init", "my-skill");

        output.MatchInlineSnapshot(
            """
            $ skillz init my-skill

            Skill already exists at my-skill/SKILL.md
            """);
    }
}
