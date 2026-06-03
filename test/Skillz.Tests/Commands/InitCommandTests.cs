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
        // Arrange
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var cmd = services.GetRequiredService<InitCommand>();

        // Act
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_workspace, "SKILL.md")));
        var content = await File.ReadAllTextAsync(
            Path.Combine(_workspace, "SKILL.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains("name:", content);
        Assert.Contains("description:", content);
    }

    [Fact]
    public async Task Init_Without_Name_Sanitizes_Directory_Derived_Skill_Name()
    {
        // Arrange - a working directory whose name is not a valid slug (spaces + uppercase).
        var messyDir = Path.Combine(_workspace, "My Skill Dir");
        Directory.CreateDirectory(messyDir);
        var services = CliTestHelper.CreateServiceProvider(workspace: messyDir, useRealFileStore: true);
        var cmd = services.GetRequiredService<InitCommand>();

        // Act
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - the scaffolded name is the sanitized slug, not the raw directory name.
        Assert.Equal(0, exitCode);
        var content = await File.ReadAllTextAsync(
            Path.Combine(messyDir, "SKILL.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains("name: my-skill-dir", content);
        Assert.DoesNotContain("My Skill Dir", content);
    }

    [Fact]
    public async Task Init_With_Name_Creates_Subdirectory_With_SkillMd()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var cmd = services.GetRequiredService<InitCommand>();

        // Act
        var parseResult = cmd.Parse(["my-skill"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        var expected = Path.Combine(_workspace, "my-skill", "SKILL.md");
        Assert.True(File.Exists(expected));
        var content = await File.ReadAllTextAsync(expected, TestContext.Current.CancellationToken);
        Assert.Contains("name: my-skill", content);
    }

    [Fact]
    public async Task Init_With_Parent_Traversal_Name_Does_Not_Escape_Cwd()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var cmd = services.GetRequiredService<InitCommand>();
        var parentSentinel = Path.Combine(Path.GetDirectoryName(_workspace)!, "escape", "SKILL.md");

        // Act
        var parseResult = cmd.Parse(["../escape"]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(parentSentinel), "SKILL.md must not be written outside the workspace");
        // Sanitized to a contained directory: "../escape" -> "escape".
        Assert.True(File.Exists(Path.Combine(_workspace, "escape", "SKILL.md")));
    }

    [Fact]
    public async Task Init_With_Absolute_Path_Name_Does_Not_Escape_Cwd()
    {
        // Arrange
        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var cmd = services.GetRequiredService<InitCommand>();
        var absoluteTarget = Path.Combine(Path.GetTempPath(), "skillz-evil-" + Guid.NewGuid().ToString("N"));

        // Act
        var parseResult = cmd.Parse([absoluteTarget]);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(absoluteTarget, "SKILL.md")));
        Assert.False(Directory.Exists(absoluteTarget));
        // Everything created stays under the workspace.
        var created = Directory.GetFiles(_workspace, "SKILL.md", SearchOption.AllDirectories);
        Assert.All(created, path => Assert.StartsWith(_workspace, Path.GetFullPath(path)));
        Assert.Single(created);
    }

    [Fact]
    public async Task Init_When_SkillMd_Exists_Leaves_File_Unchanged()
    {
        // Arrange
        var existing = Path.Combine(_workspace, "SKILL.md");
        await File.WriteAllTextAsync(existing, "existing content", TestContext.Current.CancellationToken);

        var services = CliTestHelper.CreateServiceProvider(workspace: _workspace, useRealFileStore: true);
        var cmd = services.GetRequiredService<InitCommand>();

        // Act
        var parseResult = cmd.Parse(Array.Empty<string>());
        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        var content = await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken);
        Assert.Equal("existing content", content);
    }
}
