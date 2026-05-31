using Skillz;
using Xunit;

namespace Skillz.Tests;

public class ProgramTests
{
    [Fact]
    public void StripBareTerminators_Removes_Terminator_Between_Options_And_Positionals()
    {
        Assert.Equal(
            ["add", "--agent", "codex", "owner/repo"],
            Program.StripBareTerminators(["add", "--agent", "codex", "--", "owner/repo"]));
    }

    [Fact]
    public void StripBareTerminators_Removes_Terminator_Before_OptionLike_Value()
    {
        Assert.Equal(
            ["add", "--upload-pack=sh"],
            Program.StripBareTerminators(["add", "--", "--upload-pack=sh"]));
    }

    [Fact]
    public void StripBareTerminators_Removes_Multiple_Terminators()
    {
        Assert.Equal(
            ["add", "owner/repo"],
            Program.StripBareTerminators(["--", "add", "--", "owner/repo", "--"]));
    }
}
