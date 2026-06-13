using Microsoft.Extensions.DependencyInjection;
using Skillz.Commands.Selection;
using Skillz.Install;
using Skillz.Tests.TestServices;
using Skillz.Tests.Utils;
using Spectre.Console.Testing;
using Xunit;

namespace Skillz.Tests.Commands;

public class AgentSelectorTests
{
    private static AgentRegistry Registry()
        => CliTestHelper.CreateServiceProvider().GetRequiredService<AgentRegistry>();

    private static AgentSelector Selector(TestConsole console)
        => new(console, Registry());

    [Fact]
    public async Task SelectAsync_Should_OrderRowsByDisplayName_And_NoteUniversals()
    {
        // A deliberately shuffled mix where universals and non-universals must INTERLEAVE by display
        // name, not group on top. cursor (Cursor) and warp (Warp) are universal (.agents/skills);
        // claude-code (Claude Code) and goose (Goose) are not. Sorted by DisplayName the rows are
        // Claude Code, Cursor, Goose, Warp - non, universal, non, universal: impossible if universals
        // were grouped first. Toggle the two non-universals (rows 0 and 2); the universals are mandatory
        // and always come back. The result is emitted in item (DisplayName) order, so its order proves
        // the interleave.
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.Spacebar);   // toggle row 0 (claude-code)
        console.Input.PushKey(ConsoleKey.DownArrow);  // -> row 1 (cursor, mandatory)
        console.Input.PushKey(ConsoleKey.DownArrow);  // -> row 2 (goose)
        console.Input.PushKey(ConsoleKey.Spacebar);   // toggle goose
        console.Input.PushKey(ConsoleKey.Enter);
        var selector = Selector(console);

        var selected = await selector.SelectAsync(
            ["warp", "claude-code", "goose", "cursor"], defaults: [], TestContext.Current.CancellationToken);

        Assert.Equal(["claude-code", "cursor", "goose", "warp"], selected);
        // Universal rows carry the dim note (only the note text contains ".agents").
        Assert.Contains(".agents", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectAsync_Should_ReturnMandatoryUnionDefaults_DroppingUnavailableDefaults_When_NonInteractive()
    {
        // A non-interactive console cannot drive the key loop, so the selector's WithDefault returns the
        // fallback without prompting: every mandatory universal unioned with the available defaults, in
        // item (DisplayName) order. A default that is not an available agent ("ghost") is dropped.
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = false;
        var selector = Selector(console);

        var selected = await selector.SelectAsync(
            ["claude-code", "opencode", "codex"],
            defaults: ["claude-code", "ghost"],
            TestContext.Current.CancellationToken);

        // Mandatory universals codex + opencode, unioned with the available default claude-code.
        Assert.Equal(["claude-code", "codex", "opencode"], selected);
    }

    [Fact]
    public async Task SelectAsync_Should_ReturnTogglesPlusMandatoryUniversals_When_AgentsChosen()
    {
        // The merged list is DisplayName-ordered: Claude Code (claude-code, non-universal, index 0),
        // Codex (codex, mandatory universal), OpenCode (opencode, mandatory universal). The user
        // toggles the highlighted non-universal claude-code at the top; the mandatory universals are
        // always part of the result regardless.
        var console = InteractiveConsole.Create();
        console.Input.PushKey(ConsoleKey.Spacebar); // toggle the highlighted top row (claude-code)
        console.Input.PushKey(ConsoleKey.Enter);
        var selector = Selector(console);

        var selected = await selector.SelectAsync(
            ["claude-code", "opencode", "codex"],
            defaults: [],
            TestContext.Current.CancellationToken);

        // The toggled agent plus the mandatory universals, in item (DisplayName) order.
        Assert.Equal(["claude-code", "codex", "opencode"], selected);
    }

    [Fact]
    public async Task SelectAsync_Should_ReturnEmpty_When_NoAgentsAvailable()
    {
        // Nothing to choose from - the selector short-circuits without prompting.
        var selector = Selector(InteractiveConsole.Create());

        var selected = await selector.SelectAsync([], defaults: [], TestContext.Current.CancellationToken);

        Assert.Empty(selected);
    }
}
