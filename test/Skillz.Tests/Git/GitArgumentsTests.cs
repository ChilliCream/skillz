using Skillz.Git;
using Xunit;

namespace Skillz.Tests.Git;

public class GitArgumentsTests
{
    [Fact]
    public void BuildCloneArguments_Rejects_Refs_Outside_Conservative_Allowlist()
    {
        // Act
        var ex =
            Assert.Throws<ArgumentException>(() =>
                GitArguments.BuildCloneArguments("https://example.com/repo.git", "target", "main;rm-rf")
            );

        // Assert
        Assert.Equal("ref", ex.ParamName);
    }

    [Fact]
    public void BuildCloneArguments_Inserts_DoubleDash_Before_Positional_Url()
    {
        // Act
        var args = GitArguments.BuildCloneArguments("https://example.com/repo.git", "target", "main");
        var separatorIndex = args.IndexOf("--");

        // Assert
        Assert.True(separatorIndex >= 0);
        Assert.Equal("https://example.com/repo.git", args[separatorIndex + 1]);
        Assert.Equal("target", args[separatorIndex + 2]);
    }

    [Fact]
    public void BuildLsRemoteArguments_Inserts_DoubleDash_Before_Positional_Url()
    {
        // Act
        var args = GitArguments.BuildLsRemoteArguments("https://example.com/repo.git");

        // Assert
        Assert.Equal("--", args[2]);
        Assert.Equal("https://example.com/repo.git", args[3]);
    }
}
