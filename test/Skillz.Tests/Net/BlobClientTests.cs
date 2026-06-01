using Skillz.Net;
using Xunit;

namespace Skillz.Tests.Net;

public class BlobClientTests
{
    private const string SampleTreeJson = """
        {
          "sha": "deadbeef",
          "tree": [
            { "path": "README.md", "type": "blob", "sha": "aaaa" },
            { "path": "skills", "type": "tree", "sha": "bbbb" }
          ]
        }
        """;

    public BlobClientTests()
    {
        BlobClient.ResetAuthStateForTests();
    }

    [Fact]
    public async Task Does_Not_Invoke_Token_Resolver_When_Unauth_Succeeds()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueOk(SampleTreeJson);

        var tokenProvider = new FakeGitHubTokenProvider(() => "should-not-be-called");
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var result = await client.FetchTreeAsync(
            "vercel",
            "skills",
            "main",
            path: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("deadbeef", result.Sha);
        Assert.Equal(0, tokenProvider.CallCount);
        Assert.Equal(1, handler.CallCount);
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Invokes_Token_Resolver_And_Retries_With_Auth_When_RateLimited()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueRateLimit();
        handler.EnqueueOk(SampleTreeJson);

        var tokenProvider = new FakeGitHubTokenProvider(() => "ghp_fake_token");
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var result = await client.FetchTreeAsync(
            "vercel",
            "skills",
            "main",
            path: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("deadbeef", result.Sha);
        Assert.Equal(1, tokenProvider.CallCount);
        Assert.Equal(2, handler.CallCount);
        var retryAuth = handler.Requests[1].Headers.Authorization;
        Assert.NotNull(retryAuth);
        Assert.Equal("Bearer", retryAuth.Scheme);
        Assert.Equal("ghp_fake_token", retryAuth.Parameter);
    }

    [Fact]
    public async Task Does_Not_Invoke_Token_Resolver_On_NonRateLimit_403()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueuePermissionDenied();
        handler.EnqueuePermissionDenied();
        handler.EnqueuePermissionDenied();

        var tokenProvider = new FakeGitHubTokenProvider(() => "should-not-be-called");
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var result = await client.FetchTreeAsync(
            "private",
            "repo",
            @ref: null,
            path: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        Assert.Equal(0, tokenProvider.CallCount);
    }

    [Fact]
    public async Task Returns_Null_Gracefully_When_RateLimited_And_No_Token()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueRateLimit();

        var tokenProvider = new FakeGitHubTokenProvider(() => null);
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var result = await client.FetchTreeAsync(
            "vercel",
            "skills",
            "main",
            path: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        Assert.Equal(1, tokenProvider.CallCount);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task After_RateLimit_Subsequent_Calls_Go_Directly_To_Auth()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueRateLimit();
        handler.EnqueueOk(SampleTreeJson);
        handler.EnqueueOk(
            """
            {
              "sha": "cafef00d",
              "tree": []
            }
            """);

        var tokenProvider = new FakeGitHubTokenProvider(() => "ghp_fake_token");
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var first = await client.FetchTreeAsync(
            "vercel",
            "skills",
            "main",
            path: null,
            TestContext.Current.CancellationToken);
        var second = await client.FetchTreeAsync(
            "vercel",
            "agent-skills",
            "main",
            path: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(first);
        Assert.Equal("deadbeef", first.Sha);
        Assert.NotNull(second);
        Assert.Equal("cafef00d", second.Sha);

        Assert.Equal(3, handler.CallCount);
        Assert.Equal(2, tokenProvider.CallCount);

        var secondCallAuth = handler.Requests[2].Headers.Authorization;
        Assert.NotNull(secondCallAuth);
        Assert.Equal("Bearer", secondCallAuth.Scheme);
        Assert.Equal("ghp_fake_token", secondCallAuth.Parameter);
    }

    [Fact]
    public async Task Tries_Branch_Fallback_When_Ref_Is_Null()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueNotFound();
        handler.EnqueueOk(SampleTreeJson);

        var tokenProvider = new FakeGitHubTokenProvider(() => null);
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var result = await client.FetchTreeAsync(
            "vercel",
            "skills",
            @ref: null,
            path: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("main", result.Branch);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("HEAD", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("main", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task FetchFileAsync_Returns_Content_On_Success()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueOk("hello world");

        var tokenProvider = new FakeGitHubTokenProvider(() => null);
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var content = await client.FetchFileAsync(
            "vercel",
            "skills",
            "skills/foo/SKILL.md",
            "main",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("hello world", content);
        Assert.Equal(
            "https://raw.githubusercontent.com/vercel/skills/main/skills/foo/SKILL.md",
            handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task FetchFileAsync_Returns_Null_On_404()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.EnqueueNotFound();

        var tokenProvider = new FakeGitHubTokenProvider(() => null);
        var client = new BlobClient(new FakeHttpClientFactory(handler), tokenProvider);

        // Act
        var content = await client.FetchFileAsync(
            "vercel",
            "skills",
            "missing.md",
            "main",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(content);
    }
}
