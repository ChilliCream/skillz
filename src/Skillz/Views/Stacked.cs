using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// Renders its children back-to-back, exactly as a sequence of <c>console.Write(child)</c> calls would
/// - no separators inserted. Unlike <see cref="Spectre.Console.Rows"/> (which adds a line break between
/// children, doubling the spacing around box renderables such as panels), this preserves the original
/// imperative layout, so a view can compose several panels and lines into one renderable without
/// shifting any whitespace.
/// </summary>
internal sealed class Stacked(params IRenderable[] children) : IRenderable
{
    Measurement IRenderable.Measure(RenderOptions options, int maxWidth)
    {
        var min = 0;
        var max = 0;
        foreach (var child in children)
        {
            var measurement = child.Measure(options, maxWidth);
            min = Math.Max(min, measurement.Min);
            max = Math.Max(max, measurement.Max);
        }

        return new Measurement(min, max);
    }

    IEnumerable<Segment> IRenderable.Render(RenderOptions options, int maxWidth)
        => children.SelectMany(child => child.Render(options, maxWidth));
}

/// <summary>A single line break, mirroring one <c>console.WriteLine()</c>.</summary>
internal sealed class BlankLine : IRenderable
{
    public static readonly BlankLine Instance = new();

    private BlankLine()
    {
    }

    Measurement IRenderable.Measure(RenderOptions options, int maxWidth) => new(0, 0);

    IEnumerable<Segment> IRenderable.Render(RenderOptions options, int maxWidth) => [Segment.LineBreak];
}
