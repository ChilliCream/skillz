using Skillz.Sources;
using Xunit;

namespace Skillz.Tests.Sources;

public class UrlSafetyGuardTests
{
    [Theory]
    [InlineData("https://example.com/skills")]
    [InlineData("https://docs.example.com/path/to/skills")]
    [InlineData("https://example.com:8443/skills")]
    public void IsSafeFetchTarget_Should_Return_True_When_Https_Public_Host(string url)
    {
        // Act
        var result = UrlSafetyGuard.IsSafeFetchTarget(new Uri(url, UriKind.Absolute));

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("http://example.com/skills")]
    [InlineData("ftp://example.com/skills")]
    [InlineData("file:///etc/passwd")]
    public void IsSafeFetchTarget_Should_Return_False_When_Scheme_Not_Https(string url)
    {
        // Act
        var result = UrlSafetyGuard.IsSafeFetchTarget(new Uri(url, UriKind.Absolute));

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://172.16.0.1/x")]
    [InlineData("https://172.31.255.255/x")]
    [InlineData("https://192.168.1.1/x")]
    [InlineData("https://0.0.0.0/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://[fe80::1]/x")]
    [InlineData("https://[fc00::1]/x")]
    [InlineData("https://[fd12:3456::1]/x")]
    public void IsSafeFetchTarget_Should_Return_False_When_Host_Is_Internal(string url)
    {
        // Act
        var result = UrlSafetyGuard.IsSafeFetchTarget(new Uri(url, UriKind.Absolute));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsSafeFetchTarget_Should_Return_False_When_Host_Is_Ipv4_Mapped_Ipv6_Metadata()
    {
        // Act
        var result = UrlSafetyGuard.IsSafeFetchTarget(
            new Uri("https://[::ffff:169.254.169.254]/x", UriKind.Absolute));

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("11.0.0.1")]
    [InlineData("8.8.8.8")]
    public void IsSafeHost_Should_Return_True_When_Public_Ip_Outside_Private_Ranges(string host)
    {
        // Act
        var result = UrlSafetyGuard.IsSafeHost(host);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsSafeHost_Should_Return_False_When_Host_Is_Null_Or_Empty()
    {
        // Act & Assert
        Assert.False(UrlSafetyGuard.IsSafeHost(null));
        Assert.False(UrlSafetyGuard.IsSafeHost(string.Empty));
    }

    [Fact]
    public void IsSafeHost_Should_Be_Case_Insensitive_For_Localhost()
    {
        // Act & Assert
        Assert.False(UrlSafetyGuard.IsSafeHost("LOCALHOST"));
    }
}
