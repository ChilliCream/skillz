namespace Skillz.Net;

/// <summary>
/// Thrown when a blob/tree fetch exceeds the client's internal fetch timeout. This is
/// distinct from the caller cancelling (which propagates as a plain
/// <see cref="OperationCanceledException"/>) and from a missing/private repository (which
/// returns <see langword="null"/>), so callers can report a timeout as an error rather than
/// silently bucketing it as "not found".
/// </summary>
internal sealed class BlobFetchTimeoutException(string url) : Exception($"Timed out fetching {url}.")
{
    public string Url { get; } = url;
}
