namespace Skillz.Net;

/// <summary>
/// Reads repository trees and individual file blobs over a host's REST API, letting the
/// update check compare remote skill folder hashes without a full clone.
/// </summary>
internal interface IBlobClient
{
    /// <summary>
    /// Fetches the tree for <paramref name="owner"/>/<paramref name="repo"/> at the given
    /// <paramref name="ref"/>, or <see langword="null"/> when it cannot be retrieved (for example
    /// a private, deleted, or unreachable repository). Throws <see cref="BlobFetchTimeoutException"/>
    /// when the fetch exceeds the internal timeout, so a timeout stays distinct from "missing".
    /// </summary>
    Task<RepoTree?> FetchTreeAsync(
        string owner,
        string repo,
        string? @ref,
        CancellationToken cancellationToken);
}
