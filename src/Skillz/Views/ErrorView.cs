using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// Renders a presented error as a bordered panel (title + message + optional hint). This is the
/// command boundary's counterpart to the success/failure panels: a <c>CliException</c> carrying a
/// title is shown here; a bare message stays a single red line via the console primitives.
/// </summary>
internal sealed class ErrorView : View
{
    private readonly string _title;
    private readonly string _message;
    private readonly string? _tip;

    private ErrorView(string title, string message, string? tip)
    {
        _title = title;
        _message = message;
        _tip = tip;
    }

    public static ErrorView Create(string title, string message, string? tip = null)
        => new(title, message, tip);

    protected override IRenderable Build()
    {
        var content = new StringBuilder();
        content.Append($"[red]{Markup.Escape(_message)}[/]");
        if (_tip is not null)
        {
            content.AppendLine();
            content.AppendLine();
            content.Append($"[dim]{Markup.Escape(_tip)}[/]");
        }

        return new Panel(new Markup(content.ToString()))
            .Header($"[bold red]{Markup.Escape(_title)}[/]")
            .BorderColor(Color.Red)
            .Expand();
    }
}
