namespace Skillz.Tests.TestServices;

internal sealed class TestConsoleEnvironment : ConsoleEnvironment
{
    public bool InputRedirected { get; set; }

    public bool OutputRedirected { get; set; }

    public bool ErrorRedirected { get; set; }

    public override bool IsInputRedirected => InputRedirected;

    public override bool IsOutputRedirected => OutputRedirected;

    public override bool IsErrorRedirected => ErrorRedirected;

    public override bool IsTty => !OutputRedirected && !ErrorRedirected;
}
