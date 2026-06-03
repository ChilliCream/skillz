using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Skillz.Tests.Net;

public class HttpClientDefaultsTests
{
    [Fact]
    public async Task Configured_HttpClient_Does_Not_Follow_Redirects()
    {
        // Build a client the way the app does: ConfigureHttpClientDefaults installs a primary
        // handler with AllowAutoRedirect disabled, so a 302 surfaces to the caller and the
        // redirect target is never requested (the SSRF redirect-pivot guard).
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(
            http => http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }));
        services.AddHttpClient("test");

        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

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

        // Act
        using var response = await client.GetAsync(prefix + "start", TestContext.Current.CancellationToken);

        // Assert — the 302 is surfaced, and the pivot target was never requested.
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
