using Microsoft.Extensions.DependencyInjection;
using Skillz.Git;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Tests.TestServices;
using Spectre.Console;
using Xunit;

namespace Skillz.Tests.Utils;

public class CliTestHelperTests
{
    [Fact]
    public void CreateServiceProvider_Registers_All_TestDoubles()
    {
        // Act
        var provider = CliTestHelper.CreateServiceProvider();

        // Assert
        Assert.IsType<TestGitClient>(provider.GetRequiredService<IGitClient>());
        Assert.IsType<TestBlobClient>(provider.GetRequiredService<IBlobClient>());
        Assert.IsType<TestGitHubTokenProvider>(provider.GetRequiredService<IGitHubTokenProvider>());
        Assert.IsType<AgentEnvironment>(provider.GetRequiredService<AgentEnvironment>());
        Assert.IsType<TestInstaller>(provider.GetRequiredService<ISkillInstaller>());
        Assert.IsType<TestSkillDiscovery>(provider.GetRequiredService<ISkillDiscovery>());
        Assert.IsType<TestSourceParser>(provider.GetRequiredService<ISourceParser>());
        Assert.IsType<TestProjectLockFile>(provider.GetRequiredService<IProjectLockFile>());
        Assert.IsType<TestGlobalLockFile>(provider.GetRequiredService<IGlobalLockFile>());
    }

    [Fact]
    public void CreateServiceProvider_Registers_Real_Singletons()
    {
        // Act
        var provider = CliTestHelper.CreateServiceProvider();

        // Assert
        Assert.IsType<TestConsoleEnvironment>(provider.GetRequiredService<ConsoleEnvironment>());
        Assert.IsType<CliExecutionContext>(provider.GetRequiredService<CliExecutionContext>());
        Assert.IsType<AgentRegistry>(provider.GetRequiredService<AgentRegistry>());
    }

    [Fact]
    public void Configure_Callback_Can_Override_Registrations()
    {
        // Arrange
        var custom = new TestGitClient();

        // Act
        var provider = CliTestHelper.CreateServiceProvider(configure: services =>
        {
            services.AddSingleton<IGitClient>(custom);
        });

        // Assert
        Assert.Same(custom, provider.GetRequiredService<IGitClient>());
    }

    [Fact]
    public void Workspace_Argument_Is_Available()
    {
        // Act
        var provider = CliTestHelper.CreateServiceProvider(workspace: "/tmp/ws");
        var workspace = provider.GetRequiredService<TestWorkspace>();

        // Assert
        Assert.Equal("/tmp/ws", workspace.Path);
    }

    [Fact]
    public async Task CapturingConsole_Records_ExtensionOutput()
    {
        // Arrange
        var provider = CliTestHelper.CreateServiceProvider();
        var console = provider.GetRequiredService<CapturingConsole>();

        // Act
        console.WriteLine("hello");
        console.Error("oops");
        await console.StatusAsync("loading", () => Task.CompletedTask);

        // Assert
        Assert.Contains("hello", console.OutputText, StringComparison.Ordinal);
        Assert.Contains("oops", console.OutputText, StringComparison.Ordinal);
        Assert.Contains("loading", console.OutputText, StringComparison.Ordinal);
    }
}
