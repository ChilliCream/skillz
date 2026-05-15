using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skillz.Commands;
using Skillz.Git;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Lock;
using Skillz.Net;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Spectre.Console;

namespace Skillz;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler cancelKeyHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelKeyHandler;

        EventHandler unloadHandler = (_, _) =>
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
        };
        AppDomain.CurrentDomain.ProcessExit += unloadHandler;

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args
        });

        builder.Services.AddSingleton(AnsiConsole.Console);
        builder.Services.AddSingleton<ConsoleEnvironment>();
        builder.Services.AddSingleton<CliExecutionContext>();
        builder.Services.AddSingleton<IInteractionService, ConsoleInteractionService>();
        builder.Services.AddSingleton<BannerService>();

        builder.Services.AddHttpClient(BlobClient.HttpClientName);
        builder.Services.AddHttpClient(SkillSearchClient.HttpClientName);
        builder.Services.AddHttpClient(WellKnownProvider.HttpClientName);

        builder.Services.AddSingleton<IGitClient, GitClient>();
        builder.Services.AddSingleton<IGitHubTokenProvider, GitHubTokenProvider>();
        builder.Services.AddSingleton<IBlobClient, BlobClient>();
        builder.Services.AddSingleton<ISkillSearchClient, SkillSearchClient>();

        builder.Services.AddSingleton<IAgentRegistry, AgentRegistry>();
        builder.Services.AddSingleton<IAgentEnvironmentDetector, AgentEnvironmentDetector>();
        builder.Services.AddSingleton<IXdgPaths, XdgPaths>();
        builder.Services.AddSingleton<IInstaller, Installer>();

        builder.Services.AddSingleton<IPluginManifest, PluginManifest>();
        builder.Services.AddSingleton<IPluginGrouping, PluginGrouping>();
        builder.Services.AddSingleton<ISkillDiscovery, SkillDiscovery>();

        builder.Services.AddSingleton<ISourceParser, SourceParser>();

        builder.Services.AddSingleton<IProvider, GitHubProvider>();
        builder.Services.AddSingleton<IProvider, GitLabProvider>();
        builder.Services.AddSingleton<IProvider, GitProvider>();
        builder.Services.AddSingleton<IProvider, LocalProvider>();
        builder.Services.AddSingleton<IProvider, WellKnownProvider>();
        builder.Services.AddSingleton<IProviderRegistry, ProviderRegistry>();

        builder.Services.AddSingleton<IProjectLockFile, ProjectLockFile>();
        builder.Services.AddSingleton<IGlobalLockFile, GlobalLockFile>();

        builder.Services.AddSingleton<IAddCommandPrompter, AddCommandPrompter>();
        builder.Services.AddSingleton<IRemoveCommandPrompter, RemoveCommandPrompter>();
        builder.Services.AddSingleton<IFindCommandPrompter, FindCommandPrompter>();

        builder.Services.AddSingleton(BuildRootCommand);

        using var host = builder.Build();
        await host.StartAsync(cts.Token).ConfigureAwait(false);

        try
        {
            var rootCommand = host.Services.GetRequiredService<RootCommand>();

            if (args.Length == 0)
            {
                var banner = host.Services.GetRequiredService<BannerService>();
                banner.ShowBanner();
                return ExitCodeConstants.Success;
            }

            var parseResult = rootCommand.Parse(args);
            return await parseResult.InvokeAsync(cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ExitCodeConstants.Cancelled;
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodeConstants.Failure;
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyHandler;
            AppDomain.CurrentDomain.ProcessExit -= unloadHandler;
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    private static RootCommand BuildRootCommand(IServiceProvider services)
    {
        var root = new RootCommand("Skillz - AI agent skill manager");
        root.Subcommands.Add(new AddCommand(services));
        root.Subcommands.Add(new RemoveCommand(services));
        root.Subcommands.Add(new ListCommand(services));
        root.Subcommands.Add(new InitCommand(services));
        RegisterExtraCommands(root, services);
        return root;
    }

    private static void RegisterExtraCommands(RootCommand root, IServiceProvider services)
    {
        root.Subcommands.Add(ActivatorUtilities.CreateInstance<FindCommand>(services));
        root.Subcommands.Add(ActivatorUtilities.CreateInstance<UpdateCommand>(services));
    }
}
