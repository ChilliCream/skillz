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

    // Simulates the client's internal fetch timeout: the send aborts with an
    // OperationCanceledException while the caller's token is NOT cancelled, exactly as a
    // CancelAfter-driven linked token would.
    public void EnqueueTimeout()
    {
        _responses.Enqueue(_ => throw new TaskCanceledException("Simulated internal fetch timeout."));
    }

    // A 200 that advertises a Content-Length over the cap (the body itself can be small; the
    // client must reject based on the declared length before reading).
    public void EnqueueOversizedOk(long declaredLength)
    {
        _responses.Enqueue(_ =>
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            content.Headers.ContentLength = declaredLength;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
    }

    // A 200 whose body is read as a stream with no declared Content-Length, so the only way to
    // bound it is by counting bytes mid-read. <paramref name="length"/> bytes are produced.
    public void EnqueueChunkedOk(long length)
    {
        _responses.Enqueue(_ =>
        {
            var content = new StreamContent(new EndlessStream(length));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
    }

    private sealed class EndlessStream(long length) : Stream
    {
        private long _produced;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _produced;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - _produced;
            if (remaining <= 0)
            {
                return 0;
            }

            var n = (int)Math.Min(count, remaining);
            // Whitespace is valid leading JSON, so a deserializer keeps pulling until the cap
            // aborts the read rather than failing on a malformed token first.
            Array.Fill(buffer, (byte)' ', offset, n);
            _produced += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = length - _produced;
            if (remaining <= 0)
            {
                return ValueTask.FromResult(0);
            }

            var n = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..n].Fill((byte)' ');
            _produced += n;
            return ValueTask.FromResult(n);
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
