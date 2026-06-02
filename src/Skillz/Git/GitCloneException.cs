namespace Skillz.Git;

internal sealed class GitCloneException(string message, string url, bool isTimeout = false, bool isAuthError = false) : Exception(message)
{
    public string Url { get; } = url;

    public bool IsTimeout { get; } = isTimeout;

    public bool IsAuthError { get; } = isAuthError;
}
