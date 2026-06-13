using System.Runtime.ExceptionServices;
using Spectre.Console;

namespace Skillz.Interaction;

internal static class AnsiConsoleExtensions
{
    public static void Error(this IAnsiConsole console, string message)
    {
        console.MarkupLineInterpolated($"[red]{message}[/]");
    }

    public static void Warning(this IAnsiConsole console, string message)
    {
        console.MarkupLineInterpolated($"[yellow]{message}[/]");
    }

    public static void Success(this IAnsiConsole console, string message)
    {
        console.MarkupLineInterpolated($"[green]{message}[/]");
    }

    public static void Dim(this IAnsiConsole console, string text)
    {
        console.MarkupLineInterpolated($"[dim]{text}[/]");
    }

    public static async Task<T> StatusAsync<T>(this IAnsiConsole console, string status, Func<Task<T>> action)
    {
        try
        {
            return await console.Status().StartAsync(status, _ => action());
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner)
        {
            // Spectre's Status spinner faults the action onto a background task and surfaces it
            // wrapped; unwrap here so callers see the real exception, not the wrapper.
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw; // unreachable
        }
    }

    public static async Task StatusAsync(this IAnsiConsole console, string status, Func<Task> action)
    {
        try
        {
            await console.Status().StartAsync(status, _ => action());
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
        }
    }

    // Parses its argument as Spectre markup rather than escaping it, so callers must pass only
    // literal or pre-built markup and must Markup.Escape any interpolated data themselves. The
    // escaping helpers above are the safe default; reach for this only for fixed markup such as a
    // section header or a colored banner.
    public static void MarkupLineRaw(this IAnsiConsole console, string markup)
    {
        console.MarkupLine(markup);
    }
}
