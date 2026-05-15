namespace Skillz;

internal class ConsoleEnvironment
{
    public virtual bool IsInputRedirected => Console.IsInputRedirected;

    public virtual bool IsOutputRedirected => Console.IsOutputRedirected;

    public virtual bool IsErrorRedirected => Console.IsErrorRedirected;

    public virtual bool IsTty => !IsOutputRedirected && !IsErrorRedirected;
}
