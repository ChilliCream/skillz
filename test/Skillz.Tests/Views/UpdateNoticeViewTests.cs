using Skillz.Views;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Views;

public class UpdateNoticeViewTests
{
    private static TestConsole WideConsole()
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        return console;
    }

    [Fact]
    public void Create_Should_RenderHeaderItemDetailAndAction()
    {
        var console = WideConsole();

        console.Write(UpdateNoticeView.Create(
            "1 skill(s) cannot be checked automatically:",
            [new UpdateNotice("legacy", "Private or deleted repo", "To update: skillz add x -g -y")]));

        var output = console.Output;
        Assert.Contains("1 skill(s) cannot be checked automatically:", output, StringComparison.Ordinal);
        Assert.Contains("* legacy (Private or deleted repo)", output, StringComparison.Ordinal);
        Assert.Contains("To update: skillz add x -g -y", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsecutiveSections_Should_KeepASeparatingBlankLine()
    {
        // The update command renders these notice sections back-to-back (skipped, then failed, then
        // timed out). Each section must terminate its last line so the following section keeps the
        // blank line that separates them - a regression if the view drops its trailing newline.
        var console = WideConsole();

        console.WriteLine();
        console.Write(UpdateNoticeView.Create("first:", [new UpdateNotice("alpha")]));
        console.WriteLine();
        console.Write(UpdateNoticeView.Create("second:", [new UpdateNotice("beta")]));

        var output = console.Output.Replace("\r\n", "\n");
        Assert.Contains("* alpha\n\nsecond:", output, StringComparison.Ordinal);
    }
}
