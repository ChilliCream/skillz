using Skillz.Views;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Views;

public class BannerViewTests
{
    // A view is just an IRenderable: construct it from its factory and render it to any console. No DI,
    // no command - the whole point of the view abstraction.
    private static string Render(IRenderable view)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.Write(view);
        return console.Output;
    }

    [Fact]
    public void LogoView_Should_RenderTheGradientWordmark()
    {
        var output = Render(LogoView.Create());

        // The colour markup is parsed away, leaving the ASCII-art glyphs.
        Assert.Contains("/$$$$$$", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BannerView_Should_RenderLogoCommandCheatSheetAndTryLine()
    {
        var output = Render(BannerView.Create());

        Assert.Contains("/$$$$$$", output, StringComparison.Ordinal);   // logo
        Assert.Contains("skillz add", output, StringComparison.Ordinal);
        Assert.Contains("Add a new skill", output, StringComparison.Ordinal);
        Assert.Contains("Remove installed skills", output, StringComparison.Ordinal);
        Assert.Contains("try:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CuratedHelpView_Should_RenderUsageAndEverySection()
    {
        var output = Render(CuratedHelpView.Create());

        Assert.Contains("Usage:", output, StringComparison.Ordinal);
        Assert.Contains("Commands", output, StringComparison.Ordinal);
        Assert.Contains("Options", output, StringComparison.Ordinal);
        Assert.Contains("Examples", output, StringComparison.Ordinal);
        Assert.Contains("Use 'skillz <command> --help' for detailed command-specific help.", output, StringComparison.Ordinal);
    }
}
