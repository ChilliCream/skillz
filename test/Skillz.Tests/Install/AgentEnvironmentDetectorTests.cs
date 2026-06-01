using Skillz.Install;
using Xunit;

namespace Skillz.Tests.Install;

public class AgentEnvironmentDetectorTests
{
    private const string Home = "/home/test";

    private static (AgentEnvironmentDetector detector, IAgentRegistry registry) Create(
        Dictionary<string, string?>? env = null,
        HashSet<string>? existingDirs = null,
        string? cwd = null)
    {
        env ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        existingDirs ??= new HashSet<string>(StringComparer.Ordinal);
        var cwdValue = cwd ?? "/workspace";

        var registry = new AgentRegistry(Home, name => env.GetValueOrDefault(name), existingDirs.Contains);
        var detector = new AgentEnvironmentDetector(
            registry,
            Home,
            name => env.GetValueOrDefault(name),
            existingDirs.Contains,
            () => cwdValue);
        return (detector, registry);
    }

    [Fact]
    public void GetAgentType_KnownName_ReturnsType()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.Equal("cursor", detector.GetAgentType("cursor"));
        Assert.Equal("cursor", detector.GetAgentType("cursor-cli"));
        Assert.Equal("claude-code", detector.GetAgentType("claude"));
        Assert.Equal("claude-code", detector.GetAgentType("cowork"));
        Assert.Equal("universal", detector.GetAgentType("devin"));
        Assert.Equal("replit", detector.GetAgentType("replit"));
        Assert.Equal("gemini-cli", detector.GetAgentType("gemini"));
        Assert.Equal("codex", detector.GetAgentType("codex"));
        Assert.Equal("antigravity", detector.GetAgentType("antigravity"));
        Assert.Equal("augment", detector.GetAgentType("augment-cli"));
        Assert.Equal("opencode", detector.GetAgentType("opencode"));
        Assert.Equal("github-copilot", detector.GetAgentType("github-copilot"));
    }

    [Fact]
    public void GetAgentType_UnknownName_ReturnsNull()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.Null(detector.GetAgentType("unknown-agent"));
    }

    [Fact]
    public async Task DetectAgent_NoEnv_ReturnsNotAgent()
    {
        // Arrange
        var (detector, _) = Create();

        // Act
        var result = await detector.DetectAgentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAgent);
        Assert.Null(result.Name);
    }

    [Fact]
    public async Task DetectAgent_CursorEnv_ReturnsCursor()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CURSOR_TRACE_ID"] = "abc123" };
        var (detector, _) = Create(env);

        // Act
        var result = await detector.DetectAgentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAgent);
        Assert.Equal("cursor", result.Name);
    }

    [Fact]
    public async Task DetectAgent_ClaudeCodeEnv_ReturnsClaude()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CLAUDECODE"] = "1" };
        var (detector, _) = Create(env);

        // Act
        var result = await detector.DetectAgentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAgent);
        Assert.Equal("claude", result.Name);
    }

    [Fact]
    public async Task DetectAgent_CachesResult()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CURSOR_AGENT"] = "1" };
        var (detector, _) = Create(env);

        // Act
        var first = await detector.DetectAgentAsync(TestContext.Current.CancellationToken);
        var second = await detector.DetectAgentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public async Task IsRunningInAgent_TrueWhenAgentDetected()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["REPL_ID"] = "id" };
        var (detector, _) = Create(env);

        // Act & Assert
        Assert.True(await detector.IsRunningInAgentAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsRunningInAgent_FalseWhenNoAgent()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.False(await detector.IsRunningInAgentAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAgentName_ReturnsNullWhenNotInAgent()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.Null(await detector.GetAgentNameAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAgentName_ReturnsNameWhenInAgent()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["OPENCODE_CLIENT"] = "1" };
        var (detector, _) = Create(env);

        // Act & Assert
        Assert.Equal("opencode", await detector.GetAgentNameAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DetectInstalledAgents_NoDirs_ReturnsEmpty()
    {
        // Arrange
        var (detector, _) = Create();

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_DetectsClaudeCode()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".claude") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("claude-code", installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_DetectsCursor()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".cursor") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("cursor", installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_OpenClaw_DetectsViaAnyLegacyDir()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".clawdbot") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("openclaw", installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_Replit_DetectsViaCwd()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/my-project/.replit" };
        var (detector, _) = Create(existingDirs: existing, cwd: "/my-project");

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("replit", installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_Codex_DetectsViaCodexHome()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".codex") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("codex", installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_Codex_DetectsViaEtcCodex()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/etc/codex" };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("codex", installed);
    }

    [Fact]
    public async Task DetectInstalledAgents_Universal_NeverDetected()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".agents", "skills") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = await detector.DetectInstalledAgentsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("universal", installed);
    }
}
