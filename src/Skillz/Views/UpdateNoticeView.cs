using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>One skill the update command could not act on: a name, an optional reason, and an optional
/// suggested command to run by hand.</summary>
internal sealed record UpdateNotice(string Name, string? Detail = null, string? Action = null);

/// <summary>
/// A dim header followed by a bulleted list of skills the update command skipped, failed, timed out on,
/// or cannot track - each optionally annotated with a reason and a manual fix-up command. Used for all
/// of the update command's "could not check" sections.
/// </summary>
internal sealed class UpdateNoticeView : View
{
    private readonly string _header;
    private readonly IReadOnlyList<UpdateNotice> _items;

    private UpdateNoticeView(string header, IReadOnlyList<UpdateNotice> items)
    {
        _header = header;
        _items = items;
    }

    public static UpdateNoticeView Create(string header, IReadOnlyList<UpdateNotice> items)
        => new(header, items);

    protected override IRenderable Build()
    {
        // Every line ends with a newline, mirroring the original's per-line MarkupLine/Dim calls: the
        // trailing break terminates the section so a following section keeps its separating blank line
        // (and is trimmed away by the snapshot when this is the terminal output).
        var content = new StringBuilder();
        content.Append($"[dim]{Markup.Escape(_header)}[/]\n");

        foreach (var item in _items)
        {
            content.Append($"  [grey85]*[/] {Markup.Escape(item.Name)}");
            if (item.Detail is { } detail)
            {
                content.Append($" [dim]({Markup.Escape(detail)})[/]");
            }
            content.Append('\n');

            if (item.Action is { } action)
            {
                content.Append($"[dim]    {Markup.Escape(action)}[/]\n");
            }
        }

        return new Markup(content.ToString());
    }
}
