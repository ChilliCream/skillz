using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Skillz.Tests.Net;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(
        StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public void AddRoute(
        string url,
        string body,
        string contentType = "application/json",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[url] = _ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };
            return response;
        };
    }

    public void AddRouteBytes(string url, byte[] body, string contentType, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[url] = _ =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(status) { Content = content };
        };
    }

    public void AddRouteNotFound(string url)
    {
        _routes[url] = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public void EnqueueOk(string json)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return response;
        });
    }

    public void EnqueueRateLimit()
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"message\":\"API rate limit exceeded\"}",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
            return response;
        });
    }

    public void EnqueuePermissionDenied()
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"Not Found\"}", Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "59");
            return response;
        });
    }

    public void EnqueueNotFound()
    {
        _responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(CloneRequest(request));

        if (_routes.Count > 0)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (_routes.TryGetValue(url, out var routeFactory))
            {
                return Task.FromResult(routeFactory(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No more stub responses queued");
        }

        var factory = _responses.Dequeue();
        return Task.FromResult(factory(request));
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
