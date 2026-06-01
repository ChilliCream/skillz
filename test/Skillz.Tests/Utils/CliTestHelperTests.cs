using Microsoft.Extensions.DependencyInjection;
using Skillz.Git;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Net;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Tests.TestServices;
using Xunit;

namespace Skillz.Tests.Utils;

public class CliTestHelperTests
{
    [Fact]
    public void CreateServiceProvider_Returns_Valid_Provider()
    {
        var provider = CliTestHelper.CreateServiceProvider();
        var interaction = provider.GetRequiredService<IInteractionService>();
        Assert.NotNull(interaction);
        Assert.IsType<TestInteractionService>(interaction);
    }

    [Fact]
    public void CreateServiceProvider_Registers_All_TestDoubles()
    {
        var provider = CliTestHelper.CreateServiceProvider();

        Assert.IsType<TestGitClient>(provider.GetRequiredService<IGitClient>());
        Assert.IsType<TestBlobClient>(provider.GetRequiredService<IBlobClient>());
        Assert.IsType<TestGitHubTokenProvider>(provider.GetRequiredService<IGitHubTokenProvider>());
        Assert.IsType<TestAgentEnvironmentDetector>(provider.GetRequiredService<IAgentEnvironmentDetector>());
        Assert.IsType<TestInstaller>(provider.GetRequiredService<IInstaller>());
        Assert.IsType<TestSkillDiscovery>(provider.GetRequiredService<ISkillDiscovery>());
        Assert.IsType<TestSourceParser>(provider.GetRequiredService<ISourceParser>());
        Assert.IsType<TestPluginManifest>(provider.GetRequiredService<IPluginManifest>());
        Assert.IsType<TestPluginGrouping>(provider.GetRequiredService<IPluginGrouping>());
        Assert.IsType<TestProjectLockFile>(provider.GetRequiredService<IProjectLockFile>());
        Assert.IsType<TestGlobalLockFile>(provider.GetRequiredService<IGlobalLockFile>());
    }

    [Fact]
    public void CreateServiceProvider_Registers_Real_Singletons()
    {
        var provider = CliTestHelper.CreateServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ConsoleEnvironment>());
        Assert.NotNull(provider.GetRequiredService<CliExecutionContext>());
        Assert.NotNull(provider.GetRequiredService<IAgentRegistry>());
    }

    [Fact]
    public void Configure_Callback_Can_Override_Registrations()
    {
        var custom = new TestGitClient();
        var provider = CliTestHelper.CreateServiceProvider(configure: services =>
        {
            services.AddSingleton<IGitClient>(custom);
        });

        Assert.Same(custom, provider.GetRequiredService<IGitClient>());
    }

    [Fact]
    public void Workspace_Argument_Is_Available()
    {
        var provider = CliTestHelper.CreateServiceProvider(workspace: "/tmp/ws");
        var workspace = provider.GetRequiredService<TestWorkspace>();
        Assert.Equal("/tmp/ws", workspace.Path);
    }

    [Fact]
    public async Task TestInteractionService_Records_Output()
    {
        var provider = CliTestHelper.CreateServiceProvider();
        var interaction = (TestInteractionService)provider.GetRequiredService<IInteractionService>();

        interaction.WriteLine("hello");
        interaction.WriteError("oops");
        await interaction.StatusAsync("loading", () => Task.CompletedTask);

        Assert.Contains("hello", interaction.Output);
        Assert.Contains("ERROR: oops", interaction.Output);
        Assert.Contains("STATUS: loading", interaction.Output);
    }

    [Fact]
    public async Task TestInteractionService_Uses_Callbacks_For_Prompts()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = CliTestHelper.CreateServiceProvider();
        var interaction = (TestInteractionService)provider.GetRequiredService<IInteractionService>();

        interaction.OnPrompt = (_, _) => "typed value";
        interaction.OnConfirm = (_, _) => true;

        var prompt = await interaction.PromptAsync("name?", "default", ct);
        var confirm = await interaction.ConfirmAsync("ok?", defaultValue: false, ct);

        Assert.Equal("typed value", prompt);
        Assert.True(confirm);
    }

    [Fact]
    public async Task TestInteractionService_Returns_Default_When_No_Callback()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = CliTestHelper.CreateServiceProvider();
        var interaction = (TestInteractionService)provider.GetRequiredService<IInteractionService>();

        var prompt = await interaction.PromptAsync("name?", "default", ct);
        var confirm = await interaction.ConfirmAsync("ok?", defaultValue: true, cancellationToken: ct);

        Assert.Equal("default", prompt);
        Assert.True(confirm);
    }
}
