using Skillz.Install;
using Xunit;

namespace Skillz.Tests.Install;

public class AgentEnvironmentTests
{
    private const string Home = "/home/test";

    private static (AgentEnvironment detector, AgentRegistry registry) Create(
        Dictionary<string, string?>? env = null,
        HashSet<string>? existingDirs = null,
        string? cwd = null)
    {
        env ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        existingDirs ??= new HashSet<string>(StringComparer.Ordinal);
        var cwdValue = cwd ?? "/workspace";

        var registry = new AgentRegistry(Home, name => env.GetValueOrDefault(name), existingDirs.Contains);
        var detector = new AgentEnvironment(
            registry,
            Home,
            name => env.GetValueOrDefault(name),
            existingDirs.Contains,
            () => cwdValue);
        return (detector, registry);
    }

    [Fact]
    public void FindAgentType_KnownName_ReturnsType()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.Equal("cursor", detector.FindAgentType("cursor"));
        Assert.Equal("cursor", detector.FindAgentType("cursor-cli"));
        Assert.Equal("claude-code", detector.FindAgentType("claude"));
        Assert.Equal("claude-code", detector.FindAgentType("cowork"));
        Assert.Equal("universal", detector.FindAgentType("devin"));
        Assert.Equal("replit", detector.FindAgentType("replit"));
        Assert.Equal("gemini-cli", detector.FindAgentType("gemini"));
        Assert.Equal("codex", detector.FindAgentType("codex"));
        Assert.Equal("antigravity", detector.FindAgentType("antigravity"));
        Assert.Equal("augment", detector.FindAgentType("augment-cli"));
        Assert.Equal("opencode", detector.FindAgentType("opencode"));
        Assert.Equal("github-copilot", detector.FindAgentType("github-copilot"));
    }

    [Fact]
    public void FindAgentType_UnknownName_ReturnsNull()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.Null(detector.FindAgentType("unknown-agent"));
    }

    [Fact]
    public void CurrentAgent_NoEnv_ReturnsNotAgent()
    {
        // Arrange
        var (detector, _) = Create();

        // Act
        var result = detector.CurrentAgent;

        // Assert
        Assert.False(result.IsAgent);
        Assert.Null(result.Name);
    }

    [Fact]
    public void CurrentAgent_CursorEnv_ReturnsCursor()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CURSOR_TRACE_ID"] = "abc123" };
        var (detector, _) = Create(env);

        // Act
        var result = detector.CurrentAgent;

        // Assert
        Assert.True(result.IsAgent);
        Assert.Equal("cursor", result.Name);
    }

    [Fact]
    public void CurrentAgent_ClaudeCodeEnv_ReturnsClaude()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CLAUDECODE"] = "1" };
        var (detector, _) = Create(env);

        // Act
        var result = detector.CurrentAgent;

        // Assert
        Assert.True(result.IsAgent);
        Assert.Equal("claude", result.Name);
    }

    [Fact]
    public void CurrentAgent_CachesResult()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CURSOR_AGENT"] = "1" };
        var (detector, _) = Create(env);

        // Act
        var first = detector.CurrentAgent;
        var second = detector.CurrentAgent;

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public void CurrentAgent_IsAgent_TrueWhenAgentDetected()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["REPL_ID"] = "id" };
        var (detector, _) = Create(env);

        // Act & Assert
        Assert.True(detector.CurrentAgent.IsAgent);
    }

    [Fact]
    public void CurrentAgent_IsAgent_FalseWhenNoAgent()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.False(detector.CurrentAgent.IsAgent);
    }

    [Fact]
    public void CurrentAgent_Name_ReturnsNullWhenNotInAgent()
    {
        // Arrange
        var (detector, _) = Create();

        // Act & Assert
        Assert.Null(detector.CurrentAgent.Name);
    }

    [Fact]
    public void CurrentAgent_Name_ReturnsNameWhenInAgent()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["OPENCODE_CLIENT"] = "1" };
        var (detector, _) = Create(env);

        // Act & Assert
        Assert.Equal("opencode", detector.CurrentAgent.Name);
    }

    [Fact]
    public void InstalledAgents_NoDirs_ReturnsEmpty()
    {
        // Arrange
        var (detector, _) = Create();

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Empty(installed);
    }

    [Fact]
    public void InstalledAgents_DetectsClaudeCode()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".claude") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Contains("claude-code", installed);
    }

    [Fact]
    public void InstalledAgents_DetectsCursor()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".cursor") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Contains("cursor", installed);
    }

    [Fact]
    public void InstalledAgents_OpenClaw_DetectsViaAnyLegacyDir()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".clawdbot") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Contains("openclaw", installed);
    }

    [Fact]
    public void InstalledAgents_Replit_DetectsViaCwd()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/my-project/.replit" };
        var (detector, _) = Create(existingDirs: existing, cwd: "/my-project");

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Contains("replit", installed);
    }

    [Fact]
    public void InstalledAgents_Codex_DetectsViaCodexHome()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".codex") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Contains("codex", installed);
    }

    [Fact]
    public void InstalledAgents_Codex_DetectsViaEtcCodex()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/etc/codex" };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.Contains("codex", installed);
    }

    [Fact]
    public void InstalledAgents_Universal_NeverDetected()
    {
        // Arrange
        var existing = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(Home, ".agents", "skills") };
        var (detector, _) = Create(existingDirs: existing);

        // Act
        var installed = detector.InstalledAgents;

        // Assert
        Assert.DoesNotContain("universal", installed);
    }
}
