namespace Skillz.Git;

internal sealed class GitCloneException : Exception
{
    public GitCloneException(string message, string url, bool isTimeout = false, bool isAuthError = false)
        : base(message)
    {
        Url = url;
        IsTimeout = isTimeout;
        IsAuthError = isAuthError;
    }

    public string Url { get; }

    public bool IsTimeout { get; }

    public bool IsAuthError { get; }
}
