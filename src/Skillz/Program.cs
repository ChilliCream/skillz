using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skillz.Commands;
using Skillz.Commands.Selection;
using Skillz.Git;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Locking;
using Skillz.Net;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Skillz.Utils;
using Skillz.Views;
using Spectre.Console;

namespace Skillz;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Strip bare `--` tokens — skillz has no pass-through commands, so the argument
        // terminator is meaningless here and would only confuse System.CommandLine's parsing.
        args = StripBareTerminators(args);

        using var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler cancelKeyHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelKeyHandler;

        EventHandler unloadHandler = (_, _) => { if (!cts.IsCancellationRequested)
        {
            cts.Cancel();
        } };
        AppDomain.CurrentDomain.ProcessExit += unloadHandler;

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

        builder.Services.AddSingleton(AnsiConsole.Console);
        builder.Services.AddSingleton<ConsoleEnvironment>();
        builder.Services.AddSingleton<CliExecutionContext>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.ConfigureHttpClient(client => client.MaxResponseContentBufferSize = BlobClient.MaxResponseBytes);
            http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        });

        builder.Services.AddHttpClient(BlobClient.HttpClientName);
        builder.Services.AddHttpClient(WellKnownProvider.HttpClientName);

        builder.Services.AddSingleton<IGitClient, GitClient>();
        builder.Services.AddSingleton<IGitHubTokenProvider, GitHubTokenProvider>();
        builder.Services.AddSingleton<IBlobClient, BlobClient>();

        builder.Services.AddSingleton<ISystemEnvironment, SystemEnvironment>();
        builder.Services.AddSingleton<IFileStore, SystemFileStore>();
        builder.Services.AddSingleton<AgentRegistry>();
        builder.Services.AddSingleton<AgentEnvironment>();
        builder.Services.AddSingleton<XdgPaths>();
        builder.Services.AddSingleton<ISkillInstaller, SkillInstaller>();
        builder.Services.AddTransient<InstallRecorder>();

        builder.Services.AddSingleton<PluginManifest>();
        builder.Services.AddSingleton<PluginGrouping>();
        builder.Services.AddSingleton<ISkillDiscovery, SkillDiscovery>();

        builder.Services.AddSingleton<ISourceParser, SourceParser>();

        builder.Services.AddSingleton<IProvider, GitHubProvider>();
        builder.Services.AddSingleton<IProvider, GitLabProvider>();
        builder.Services.AddSingleton<IProvider, GitProvider>();
        builder.Services.AddSingleton<IProvider, LocalProvider>();
        builder.Services.AddSingleton<IProvider, WellKnownProvider>();
        builder.Services.AddSingleton<ProviderRegistry>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IProjectLockFile, ProjectLockFile>();
        builder.Services.AddSingleton<IGlobalLockFile, GlobalLockFile>();

        builder.Services.AddSingleton<ISkillSelector, SkillSelector>();
        builder.Services.AddSingleton<IAgentSelector, AgentSelector>();

        builder.Services.AddTransient<AddCommand>();
        builder.Services.AddTransient<RemoveCommand>();
        builder.Services.AddTransient<ListCommand>();
        builder.Services.AddTransient<InitCommand>();
        builder.Services.AddTransient<UpdateCommand>();
        builder.Services.AddTransient<SkillzRootCommand>();

        using var host = builder.Build();

        try
        {
            await host.StartAsync(cts.Token);

            var rootCommand = host.Services.GetRequiredService<SkillzRootCommand>();

            if (args.Length == 0)
            {
                if (ShouldShowBanner(host.Services))
                {
                    host.Services.GetRequiredService<IAnsiConsole>().Write(BannerView.Create());
                }
                return ExitCodeConstants.Success;
            }

            // Curated root help: short-circuit when user asks for top-level help only
            if (args.Length == 1 && args[0] is "--help" or "-h" or "-?")
            {
                var console = host.Services.GetRequiredService<IAnsiConsole>();
                if (ShouldShowBanner(host.Services))
                {
                    console.Write(LogoView.Create());
                }
                console.Write(CuratedHelpView.Create());
                return ExitCodeConstants.Success;
            }

            var parseResult = rootCommand.Parse(args);

            var commandName = parseResult.CommandResult.Command.Name;
            if (commandName is "add" or "init" && ShouldShowBanner(host.Services))
            {
                host.Services.GetRequiredService<IAnsiConsole>().Write(LogoView.Create());
            }

            return await parseResult.InvokeAsync(cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            return ExitCodeConstants.Cancelled;
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
            await host.StopAsync();
        }
    }

    internal static string[] StripBareTerminators(string[] args) => args.Where(a => a != "--").ToArray();

    // The banner and logo are chrome: skip them for machine-readable JSON output and when running
    // inside an agent host, matching the old BannerView.ShouldSkip guard.
    private static bool ShouldShowBanner(IServiceProvider services)
    {
        var context = services.GetRequiredService<CliExecutionContext>();
        var agentEnvironment = services.GetRequiredService<AgentEnvironment>();
        return !context.IsJsonOutput && !agentEnvironment.IsRunningInsideAgent;
    }
}
