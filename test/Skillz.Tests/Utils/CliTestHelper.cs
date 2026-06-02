using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Git;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Skillz.Tests.TestServices;

namespace Skillz.Tests.Utils;

internal static class CliTestHelper
{
    public static IServiceProvider CreateServiceProvider(
        string? workspace = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<TestConsoleEnvironment>();
        services.AddSingleton<ConsoleEnvironment>(sp => sp.GetRequiredService<TestConsoleEnvironment>());
        services.AddSingleton<CliExecutionContext>();

        services.AddSingleton<TestInteractionService>();
        services.AddSingleton<IInteractionService>(sp => sp.GetRequiredService<TestInteractionService>());

        services.AddSingleton<TestGitClient>();
        services.AddSingleton<IGitClient>(sp => sp.GetRequiredService<TestGitClient>());

        services.AddSingleton<TestBlobClient>();
        services.AddSingleton<IBlobClient>(sp => sp.GetRequiredService<TestBlobClient>());

        services.AddSingleton<TestGitHubTokenProvider>();
        services.AddSingleton<IGitHubTokenProvider>(sp => sp.GetRequiredService<TestGitHubTokenProvider>());

        services.AddSingleton<AgentRegistry>();

        services.AddSingleton<TestAgentEnvironmentDetector>();
        services.AddSingleton<IAgentEnvironmentDetector>(sp => sp.GetRequiredService<TestAgentEnvironmentDetector>());

        services.AddSingleton<TestInstaller>();
        services.AddSingleton<ISkillInstaller>(sp => sp.GetRequiredService<TestInstaller>());

        services.AddSingleton<TestSkillDiscovery>();
        services.AddSingleton<ISkillDiscovery>(sp => sp.GetRequiredService<TestSkillDiscovery>());

        services.AddSingleton<TestSourceParser>();
        services.AddSingleton<ISourceParser>(sp => sp.GetRequiredService<TestSourceParser>());

        services.AddSingleton<TestPluginManifest>();
        services.AddSingleton<IPluginManifest>(sp => sp.GetRequiredService<TestPluginManifest>());

        services.AddSingleton<TestPluginGrouping>();
        services.AddSingleton<IPluginGrouping>(sp => sp.GetRequiredService<TestPluginGrouping>());

        services.AddSingleton<TestProjectLockFile>();
        services.AddSingleton<IProjectLockFile>(sp => sp.GetRequiredService<TestProjectLockFile>());

        services.AddSingleton<TestGlobalLockFile>();
        services.AddSingleton<IGlobalLockFile>(sp => sp.GetRequiredService<TestGlobalLockFile>());

        services.AddSingleton<TestAddCommandPrompter>();
        services.AddSingleton<IAddCommandPrompter>(sp => sp.GetRequiredService<TestAddCommandPrompter>());
        services.AddSingleton<TestRemoveCommandPrompter>();
        services.AddSingleton<IRemoveCommandPrompter>(sp => sp.GetRequiredService<TestRemoveCommandPrompter>());

        services.AddSingleton<IProvider>(sp => new GitHubProvider(
            sp.GetRequiredService<IGitClient>(),
            sp.GetRequiredService<ISkillDiscovery>()));
        services.AddSingleton<IProvider>(sp => new GitLabProvider(
            sp.GetRequiredService<IGitClient>(),
            sp.GetRequiredService<ISkillDiscovery>()));
        services.AddSingleton<IProvider>(sp => new GitProvider(
            sp.GetRequiredService<IGitClient>(),
            sp.GetRequiredService<ISkillDiscovery>()));
        services.AddSingleton<IProvider>(sp => new LocalProvider(sp.GetRequiredService<ISkillDiscovery>()));
        services.AddSingleton<ProviderRegistry>();

        services.AddTransient<AddCommandExecutor>();
        services.AddTransient<AddCommand>();
        services.AddTransient<RemoveCommand>();
        services.AddTransient<ListCommand>();
        services.AddTransient<InitCommand>();
        services.AddTransient<UpdateCommand>();
        services.AddTransient<SkillzRootCommand>();

        if (workspace is not null)
        {
            services.AddSingleton(new TestWorkspace(workspace));
        }

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}

internal sealed record TestWorkspace(string Path);
