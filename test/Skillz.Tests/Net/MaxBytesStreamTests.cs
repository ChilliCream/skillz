using System.Text;
using Skillz.Net;
using Xunit;

namespace Skillz.Tests.Net;

public class MaxBytesStreamTests
{
    [Fact]
    public async Task Reads_Through_When_Under_Cap()
    {
        // Arrange
        var payload = "hello world"u8.ToArray();
        await using var inner = new MemoryStream(payload);
        await using var bounded = new MaxBytesStream(inner, maxBytes: 1024);
        using var reader = new StreamReader(bounded);

        // Act
        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("hello world", text);
    }

    [Fact]
    public async Task Throws_When_Total_Read_Exceeds_Cap()
    {
        // Arrange: more bytes available than the cap; reading them all must abort.
        var payload = Encoding.ASCII.GetBytes("0123456789");
        await using var inner = new MemoryStream(payload);
        await using var bounded = new MaxBytesStream(inner, maxBytes: 4);

        // Act + Assert: draining the stream past the cap throws.
        await Assert.ThrowsAsync<HttpRequestException>(
            async () =>
            {
                var buffer = new byte[2];
                while (await bounded.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
                {
                }
            });
    }

    [Fact]
    public async Task Allows_Reading_Exactly_Up_To_Cap()
    {
        // Arrange
        var payload = Encoding.ASCII.GetBytes("abcd");
        await using var inner = new MemoryStream(payload);
        await using var bounded = new MaxBytesStream(inner, maxBytes: 4);
        var buffer = new byte[4];

        // Act
        await bounded.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("abcd", Encoding.ASCII.GetString(buffer));
    }
}
