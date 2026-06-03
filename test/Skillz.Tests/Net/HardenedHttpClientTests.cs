using System.Net;
using Skillz;
using Xunit;

namespace Skillz.Tests.Net;

public class HardenedHttpClientTests
{
    [Fact]
    public void Hardened_Primary_Handler_Disables_AutoRedirect()
    {
        // Arrange + Act
        using var handler = Program.CreateHardenedPrimaryHandler();

        // Assert
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task Hardened_Client_Does_Not_Follow_Redirect()
    {
        // Arrange: a listener that 302-redirects to a "pivot" target. With auto-redirect
        // disabled the client must surface the 302 itself and never request the target.
        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var requestedPaths = new List<string>();
        var serverTask = Task.Run(
            async () =>
        {
            // Serve at most two requests: the initial GET and (only if the client misbehaves)
            // a follow-up to the redirect target.
            for (var i = 0; i < 2; i++)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                lock (requestedPaths)
                {
                    requestedPaths.Add(context.Request.Url!.AbsolutePath);
                }

                if (context.Request.Url!.AbsolutePath == "/start")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Found;
                    context.Response.RedirectLocation = prefix + "pivot";
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }

                context.Response.Close();
            }
        },
            TestContext.Current.CancellationToken);

        using var handler = Program.CreateHardenedPrimaryHandler();
        using var client = new HttpClient(handler);

        // Act
        using var response = await client.GetAsync(prefix + "start", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        lock (requestedPaths)
        {
            Assert.Equal(["/start"], requestedPaths);
            Assert.DoesNotContain("/pivot", requestedPaths);
        }

        listener.Stop();
        await serverTask;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
