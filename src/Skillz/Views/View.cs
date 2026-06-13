using Spectre.Console.Rendering;

namespace Skillz.Views;

/// <summary>
/// Base class for views. A subclass exposes a static factory that captures its data and implements
/// <see cref="Build"/> to compose the content; the base adapts that to <see cref="IRenderable"/> so
/// <c>console.Write(view)</c> renders it. <see cref="Build"/> is invoked lazily and cached, so the
/// measure and render passes share a single composition.
/// </summary>
internal abstract class View : IView
{
    private IRenderable? _content;

    private IRenderable Content => _content ??= Build();

    /// <summary>Composes the view's content. Called at most once per instance.</summary>
    protected abstract IRenderable Build();

    Measurement IRenderable.Measure(RenderOptions options, int maxWidth)
        => Content.Measure(options, maxWidth);

    IEnumerable<Segment> IRenderable.Render(RenderOptions options, int maxWidth)
        => Content.Render(options, maxWidth);
}
