using Spectre.Console.Testing;

namespace Skillz.Tests.TestServices;

/// <summary>
/// Builds a Spectre <see cref="TestConsole"/> that can drive the real interactive prompts the way a
/// terminal does: the key loop is enabled, ANSI is on (the prompts need it to run), and input is
/// scriptable via <c>console.Input.PushKey/PushText</c>. The width is generous so prompt titles and
/// confirmation summaries can be asserted as substrings without line-wrapping splitting them. Use
/// this for tests that exercise a selector or command's interactive path; clean snapshot/output
/// tests keep the non-interactive <see cref="CapturingConsole"/>.
/// </summary>
internal static class InteractiveConsole
{
    public static TestConsole Create(int width = 200)
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        console.Profile.Width = width;
        return console;
    }
}
