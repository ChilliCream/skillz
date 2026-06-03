using Skillz.Git;
using Xunit;

namespace Skillz.Tests.Git;

public class GitUrlTests
{
    [Fact]
    public void RedactUrlUserInfo_Removes_Credentials_From_Messages()
    {
        // Act
        var redacted = GitUrl.RedactUrlUserInfo("fatal: https://user:secret@example.com/owner/repo.git failed");

        // Assert
        Assert.DoesNotContain("user:secret", redacted);
        Assert.Contains("https://<redacted>@example.com/owner/repo.git", redacted);
    }

    [Fact]
    public void RedactUrlUserInfo_Should_Redact_Whole_Userinfo_When_Password_Contains_At()
    {
        // Arrange - a password that itself contains '@' must not leak the trailing fragment.
        var url = "https://user:p@ss@host.com/owner/repo.git";

        // Act
        var redacted = GitUrl.RedactUrlUserInfo(url);

        // Assert
        Assert.Equal("https://<redacted>@host.com/owner/repo.git", redacted);
        Assert.DoesNotContain("ss@host", redacted);
        Assert.DoesNotContain("p@ss", redacted);
    }

    [Fact]
    public void RedactUrlUserInfo_Should_Leave_Url_Unchanged_When_No_Credentials()
    {
        // Arrange
        var url = "https://github.com/owner/repo.git";

        // Act
        var redacted = GitUrl.RedactUrlUserInfo(url);

        // Assert
        Assert.Equal(url, redacted);
    }

    [Fact]
    public void RedactUrlUserInfo_Should_Not_Redact_When_At_Is_In_Path()
    {
        // Arrange - '@' after the first '/' is part of the path, not credentials.
        var url = "https://host.com/p@th/repo.git";

        // Act
        var redacted = GitUrl.RedactUrlUserInfo(url);

        // Assert
        Assert.Equal(url, redacted);
    }

    [Fact]
    public void StripUserInfo_Should_Remove_Whole_Userinfo_When_Password_Contains_At()
    {
        // Arrange
        var url = "https://user:p@ss@host.com/owner/repo.git";

        // Act
        var stripped = GitUrl.StripUserInfo(url);

        // Assert
        Assert.Equal("https://host.com/owner/repo.git", stripped);
        Assert.DoesNotContain("user", stripped);
        Assert.DoesNotContain("p@ss", stripped);
        Assert.DoesNotContain("@", stripped);
    }

    [Fact]
    public void StripUserInfo_Should_Remove_Single_Credential_When_Userinfo_Present()
    {
        // Arrange
        var url = "https://token@github.com/owner/repo.git";

        // Act
        var stripped = GitUrl.StripUserInfo(url);

        // Assert
        Assert.Equal("https://github.com/owner/repo.git", stripped);
    }

    [Fact]
    public void StripUserInfo_Should_Leave_Url_Unchanged_When_No_Credentials()
    {
        // Arrange
        var url = "https://github.com/owner/repo.git";

        // Act
        var stripped = GitUrl.StripUserInfo(url);

        // Assert
        Assert.Equal(url, stripped);
    }

    [Fact]
    public void StripUserInfo_Should_Not_Strip_When_At_Is_In_Path()
    {
        // Arrange
        var url = "https://host.com/p@th/repo.git";

        // Act
        var stripped = GitUrl.StripUserInfo(url);

        // Assert
        Assert.Equal(url, stripped);
    }
}
