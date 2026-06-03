using Skillz;
using Xunit;

namespace Skillz.Tests;

public class ContainsControlCharacterTests
{
    [Theory]
    [InlineData("\u001b")] // ESC
    [InlineData("\t")] // TAB
    [InlineData("\r")] // CR
    [InlineData("\n")] // LF
    [InlineData("\0")] // NUL
    [InlineData("\u007f")] // DEL
    [InlineData("\u0085")] // NEL (C1) -- flagged by char.IsControl but not the old C0/DEL set
    [InlineData("repo\u001bname")]
    public void ContainsControlCharacter_Should_ReturnTrue_When_ValueHasControlByte(string value)
    {
        // Act & Assert
        Assert.True(value.ContainsControlCharacter());
    }

    [Theory]
    [InlineData("owner/repo")]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("café")]
    [InlineData("中文")]
    [InlineData("")]
    public void ContainsControlCharacter_Should_ReturnFalse_When_ValueIsPrintable(string value)
    {
        // Act & Assert
        Assert.False(value.ContainsControlCharacter());
    }
}
