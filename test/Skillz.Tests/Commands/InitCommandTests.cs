using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Tests.Utils;
using Xunit;

namespace Skillz.Tests.Commands;

[Collection(CommandTestCollection.Name)]
public class InitCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _originalCwd;

    public InitCommandTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "skillz-init-" + Guid.NewGuid().ToString("N"));
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
    public async Task Init_Without_Name_Creates_SkillMd_In_Cwd()
    {
        var services = CliTestHelper.CreateServiceProvider();
        var cmd = services.GetRequiredService<InitCommand>();

        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_workspace, "SKILL.md")));
        var content = await File.ReadAllTextAsync(Path.Combine(_workspace, "SKILL.md"), TestContext.Current.CancellationToken);
        Assert.Contains("name:", content);
        Assert.Contains("description:", content);
    }

    [Fact]
    public async Task Init_With_Name_Creates_Subdirectory_With_SkillMd()
    {
        var services = CliTestHelper.CreateServiceProvider();
        var cmd = services.GetRequiredService<InitCommand>();

        var parseResult = cmd.Parse(["my-skill"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var expected = Path.Combine(_workspace, "my-skill", "SKILL.md");
        Assert.True(File.Exists(expected));
        var content = await File.ReadAllTextAsync(expected, TestContext.Current.CancellationToken);
        Assert.Contains("name: my-skill", content);
    }

    [Fact]
    public async Task Init_When_SkillMd_Exists_Leaves_File_Unchanged()
    {
        var existing = Path.Combine(_workspace, "SKILL.md");
        await File.WriteAllTextAsync(existing, "existing content", TestContext.Current.CancellationToken);

        var services = CliTestHelper.CreateServiceProvider();
        var cmd = services.GetRequiredService<InitCommand>();

        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var content = await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken);
        Assert.Equal("existing content", content);
    }
}
