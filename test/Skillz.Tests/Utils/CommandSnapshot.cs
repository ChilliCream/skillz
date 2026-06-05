using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Tests.TestServices;
using Xunit;

namespace Skillz.Tests.Utils;

/// <summary>
/// Runs a Skillz command end-to-end through the root command and captures everything a user would
/// see - the rendered console output, raw stdout, stderr, and a non-zero exit code - as a single
/// deterministic block suitable for inline snapshotting. The first line echoes the invoked command
/// so a snapshot reads like a terminal session:
/// <code>
/// $ skillz list
///
/// No project skills found.
/// Try listing global skills with -g
/// </code>
/// </summary>
internal static class CommandSnapshot
{
    public static async Task<string> RunAsync(IServiceProvider services, params string[] args)
    {
        var root = services.GetRequiredService<SkillzRootCommand>();
        var interaction = (TestInteractionService)services.GetRequiredService<IInteractionService>();

        // Scrub absolute paths back to stable tokens using the SAME ambient state the command builds
        // its paths from - the injected ISystemEnvironment - rather than the real process. On macOS the
        // two diverge: the temp workspace is rooted under /var/folders/... but Directory.GetCurrentDirectory()
        // returns it symlink-resolved as /private/var/folders/..., so scrubbing by the process cwd would
        // miss the /var form that actually appears in the output. Reading from ISystemEnvironment keeps the
        // tokens matching the output on every platform.
        var systemEnvironment = services.GetRequiredService<ISystemEnvironment>();
        var cwd = systemEnvironment.CurrentDirectory;
        var home = systemEnvironment.HomeDirectory;

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        int exitCode;
        try
        {
            exitCode = await root.Parse(args)
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return Render(args, exitCode, interaction.OutputText, stdout.ToString(), stderr.ToString(), cwd, home);
    }

    private static string Render(
        string[] args,
        int exitCode,
        string rendered,
        string stdout,
        string stderr,
        string cwd,
        string home)
    {
        var builder = new StringBuilder();

        builder.Append("$ skillz");
        foreach (var arg in args)
        {
            builder.Append(' ').Append(arg);
        }
        builder.Append('\n');

        if (exitCode != 0)
        {
            builder.Append("# exit ").Append(exitCode).Append('\n');
        }

        builder.Append('\n');

        // Human-facing output (interaction) renders to its own writer; raw stdout carries machine
        // output such as JSON. Only one is populated for a given command, so concatenation is stable.
        var body = Scrub(rendered + stdout, cwd, home).TrimEnd();
        builder.Append(body);

        var error = Scrub(stderr, cwd, home).Trim();
        if (error.Length > 0)
        {
            if (body.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("[stderr]\n").Append(error);
        }

        return builder.ToString().TrimEnd();
    }

    private static string Scrub(string value, string cwd, string home)
    {
        value = value.Replace("\r\n", "\n");

        // Replace the (more specific) working directory before home in case one nests the other.
        if (cwd.Length > 0)
        {
            value = value.Replace(cwd, "<cwd>");
        }

        if (home.Length > 0)
        {
            value = value.Replace(home, "~");
        }

        return value;
    }
}
