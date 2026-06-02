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
}
