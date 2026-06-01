using Skillz.Install;
using Xunit;

namespace Skillz.Tests.Install;

public class AgentRegistryTests
{
    private static AgentRegistry CreateRegistry(string home = "/home/test")
    {
        return new AgentRegistry(home, _ => null, _ => false);
    }

    [Fact]
    public void All_ContainsAtLeastFiftyAgents()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.True(registry.All.Count >= 50, $"Expected at least 50 agents, got {registry.All.Count}");
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("cursor")]
    [InlineData("codex")]
    [InlineData("opencode")]
    [InlineData("amp")]
    [InlineData("goose")]
    [InlineData("gemini-cli")]
    [InlineData("github-copilot")]
    [InlineData("antigravity")]
    [InlineData("warp")]
    [InlineData("windsurf")]
    [InlineData("cline")]
    [InlineData("roo")]
    [InlineData("droid")]
    [InlineData("replit")]
    [InlineData("openclaw")]
    [InlineData("adal")]
    [InlineData("universal")]
    public void All_ContainsExpectedAgent(string agentType)
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.True(registry.All.ContainsKey(agentType));
    }

    [Fact]
    public void GetConfig_KnownAgent_ReturnsConfig()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("claude-code");

        // Assert
        Assert.Equal("claude-code", config.Name);
        Assert.Equal("Claude Code", config.DisplayName);
        Assert.Equal(".claude/skills", config.SkillsDir);
    }

    [Fact]
    public void GetConfig_UnknownAgent_Throws()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetConfig("does-not-exist"));
    }

    [Fact]
    public void TryGetConfig_KnownAgent_ReturnsTrue()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.True(registry.TryGetConfig("cursor", out var config));
        Assert.NotNull(config);
        Assert.Equal("cursor", config!.Name);
    }

    [Fact]
    public void TryGetConfig_UnknownAgent_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.False(registry.TryGetConfig("nope", out var config));
        Assert.Null(config);
    }

    [Theory]
    [InlineData("cursor", true)]
    [InlineData("amp", true)]
    [InlineData("antigravity", true)]
    [InlineData("codex", true)]
    [InlineData("opencode", true)]
    [InlineData("warp", true)]
    [InlineData("cline", true)]
    [InlineData("dexto", true)]
    [InlineData("claude-code", false)]
    [InlineData("openclaw", false)]
    [InlineData("droid", false)]
    [InlineData("crush", false)]
    public void IsUniversalAgent_MatchesSkillsDir(string agentType, bool isUniversal)
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.Equal(isUniversal, registry.IsUniversalAgent(agentType));
    }

    [Fact]
    public void IsUniversalAgent_UnknownAgent_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.False(registry.IsUniversalAgent("unknown"));
    }

    [Fact]
    public void UniversalAgents_ExcludesAgentsWithShowInUniversalListFalse()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var universalAgents = registry.UniversalAgents;

        // Assert
        Assert.DoesNotContain("replit", universalAgents);
        Assert.DoesNotContain("universal", universalAgents);
    }

    [Fact]
    public void UniversalAgents_IncludesAgentsThatShareTheAgentsSkillsDir()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var universalAgents = registry.UniversalAgents;

        // Assert
        Assert.Contains("cursor", universalAgents);
        Assert.Contains("amp", universalAgents);
        Assert.Contains("opencode", universalAgents);
        Assert.Contains("codex", universalAgents);
    }

    [Fact]
    public void NonUniversalAgents_ContainsAgentsWithCustomSkillsDir()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var nonUniversal = registry.NonUniversalAgents;

        // Assert
        Assert.Contains("claude-code", nonUniversal);
        Assert.Contains("openclaw", nonUniversal);
        Assert.Contains("droid", nonUniversal);
    }

    [Fact]
    public void NonUniversalAgents_DoesNotContainUniversalAgents()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var nonUniversal = registry.NonUniversalAgents;

        // Assert
        Assert.DoesNotContain("cursor", nonUniversal);
        Assert.DoesNotContain("amp", nonUniversal);
    }

    [Fact]
    public void AgentTypes_ReturnsAllAgentKeys()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var keys = registry.AgentTypes;

        // Assert
        Assert.Equal(registry.All.Count, keys.Length);
        Assert.Contains("claude-code", keys);
    }

    [Fact]
    public void ReplitConfig_HasShowInUniversalListFalse()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("replit");

        // Assert
        Assert.False(config.ShowInUniversalList);
    }

    [Fact]
    public void UniversalConfig_HasShowInUniversalListFalse()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("universal");

        // Assert
        Assert.False(config.ShowInUniversalList);
    }

    [Fact]
    public void OpenClawSkillsDir_IsLegacyNonUniversalRoot()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("openclaw");

        // Assert
        Assert.Equal("skills", config.SkillsDir);
    }

    [Fact]
    public void GetOpenClawGlobalSkillsDir_PrefersOpenClawWhenPresent()
    {
        // Arrange
        const string home = "/tmp/home";
        var openClawDir = Path.Combine(home, ".openclaw");
        var clawdbotDir = Path.Combine(home, ".clawdbot");
        var moltbotDir = Path.Combine(home, ".moltbot");

        bool Exists(string path) => path == openClawDir || path == clawdbotDir || path == moltbotDir;

        // Act & Assert
        Assert.Equal(Path.Combine(home, ".openclaw", "skills"), AgentRegistry.GetOpenClawGlobalSkillsDir(home, Exists));
    }

    [Fact]
    public void GetOpenClawGlobalSkillsDir_FallsBackToClawdbot()
    {
        // Arrange
        const string home = "/tmp/home";
        var clawdbotDir = Path.Combine(home, ".clawdbot");
        var moltbotDir = Path.Combine(home, ".moltbot");

        bool Exists(string path) => path == clawdbotDir || path == moltbotDir;

        // Act & Assert
        Assert.Equal(Path.Combine(home, ".clawdbot", "skills"), AgentRegistry.GetOpenClawGlobalSkillsDir(home, Exists));
    }

    [Fact]
    public void GetOpenClawGlobalSkillsDir_FallsBackToMoltbot()
    {
        // Arrange
        const string home = "/tmp/home";
        var moltbotDir = Path.Combine(home, ".moltbot");

        bool Exists(string path) => path == moltbotDir;

        // Act & Assert
        Assert.Equal(Path.Combine(home, ".moltbot", "skills"), AgentRegistry.GetOpenClawGlobalSkillsDir(home, Exists));
    }

    [Fact]
    public void GetOpenClawGlobalSkillsDir_DefaultsToOpenClawWhenNothingExists()
    {
        // Arrange
        const string home = "/tmp/home";

        // Act & Assert
        Assert.Equal(
            Path.Combine(home, ".openclaw", "skills"),
            AgentRegistry.GetOpenClawGlobalSkillsDir(home, _ => false));
    }

    [Fact]
    public void ClaudeCodeConfig_UsesClaudeConfigDirEnvWhenSet()
    {
        // Arrange
        const string home = "/home/test";
        var envVars = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CONFIG_DIR"] = "/custom/claude"
        };

        // Act
        var registry = new AgentRegistry(home, name => envVars.GetValueOrDefault(name), _ => false);
        var config = registry.GetConfig("claude-code");

        // Assert
        Assert.Equal(Path.Combine("/custom/claude", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void CodexConfig_UsesCodexHomeEnvWhenSet()
    {
        // Arrange
        const string home = "/home/test";
        var envVars = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CODEX_HOME"] = "/custom/codex" };

        // Act
        var registry = new AgentRegistry(home, name => envVars.GetValueOrDefault(name), _ => false);
        var config = registry.GetConfig("codex");

        // Assert
        Assert.Equal(Path.Combine("/custom/codex", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void MistralVibeConfig_UsesVibeHomeEnvWhenSet()
    {
        // Arrange
        const string home = "/home/test";
        var envVars = new Dictionary<string, string?>(StringComparer.Ordinal) { ["VIBE_HOME"] = "/custom/vibe" };

        // Act
        var registry = new AgentRegistry(home, name => envVars.GetValueOrDefault(name), _ => false);
        var config = registry.GetConfig("mistral-vibe");

        // Assert
        Assert.Equal(Path.Combine("/custom/vibe", "skills"), config.GlobalSkillsDir);
    }
}
