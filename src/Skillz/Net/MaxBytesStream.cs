namespace Skillz.Net;

/// <summary>
/// A read-only wrapper that aborts once more than <paramref name="maxBytes"/> have been read
/// from <paramref name="inner"/>. Used to bound streamed responses (e.g. JSON deserialization)
/// where <c>HttpClient.MaxResponseContentBufferSize</c> does not apply because the body is read
/// as a stream rather than buffered.
/// </summary>
internal sealed class MaxBytesStream(Stream inner, long maxBytes) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        Track(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        Track(n);
        return n;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Track(n);
        return n;
    }

    private void Track(int n)
    {
        if (n <= 0)
        {
            return;
        }

        _read += n;
        if (_read > maxBytes)
        {
            throw new HttpRequestException($"Response exceeded the {maxBytes}-byte cap.");
        }
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
