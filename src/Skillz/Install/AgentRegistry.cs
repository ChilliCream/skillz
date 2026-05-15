using System.Collections.Immutable;

namespace Skillz.Install;

internal sealed class AgentRegistry : IAgentRegistry
{
    public const string UniversalSkillsDir = ".agents/skills";

    private readonly ImmutableDictionary<string, AgentConfig> _agents;

    public AgentRegistry()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable, Directory.Exists)
    {
    }

    public AgentRegistry(string home, Func<string, string?> envReader, Func<string, bool> directoryExists)
    {
        _agents = BuildAgents(home, envReader, directoryExists);
    }

    public ImmutableDictionary<string, AgentConfig> All => _agents;

    public AgentConfig GetConfig(string agentType)
    {
        if (!_agents.TryGetValue(agentType, out var config))
        {
            throw new ArgumentException($"Unknown agent type: {agentType}", nameof(agentType));
        }

        return config;
    }

    public bool TryGetConfig(string agentType, out AgentConfig? config)
    {
        if (_agents.TryGetValue(agentType, out var found))
        {
            config = found;
            return true;
        }

        config = null;
        return false;
    }

    public IReadOnlyList<string> ListAgentTypes()
    {
        return [.. _agents.Keys];
    }

    public IReadOnlyList<string> GetUniversalAgents()
    {
        return [.. _agents
            .Where(kv => kv.Value.SkillsDir == UniversalSkillsDir && kv.Value.ShowInUniversalList)
            .Select(kv => kv.Key)];
    }

    public IReadOnlyList<string> GetNonUniversalAgents()
    {
        return [.. _agents
            .Where(kv => kv.Value.SkillsDir != UniversalSkillsDir)
            .Select(kv => kv.Key)];
    }

    public bool IsUniversalAgent(string agentType)
    {
        return _agents.TryGetValue(agentType, out var config)
            && config.SkillsDir == UniversalSkillsDir;
    }

    public static string GetOpenClawGlobalSkillsDir(string home, Func<string, bool> directoryExists)
    {
        if (directoryExists(Path.Combine(home, ".openclaw")))
        {
            return Path.Combine(home, ".openclaw", "skills");
        }

        if (directoryExists(Path.Combine(home, ".clawdbot")))
        {
            return Path.Combine(home, ".clawdbot", "skills");
        }

        if (directoryExists(Path.Combine(home, ".moltbot")))
        {
            return Path.Combine(home, ".moltbot", "skills");
        }

        return Path.Combine(home, ".openclaw", "skills");
    }

    private static ImmutableDictionary<string, AgentConfig> BuildAgents(
        string home,
        Func<string, string?> envReader,
        Func<string, bool> directoryExists)
    {
        var configHome = ResolveEnv(envReader, "XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        var codexHome = ResolveEnv(envReader, "CODEX_HOME") ?? Path.Combine(home, ".codex");
        var claudeHome = ResolveEnv(envReader, "CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        var vibeHome = ResolveEnv(envReader, "VIBE_HOME") ?? Path.Combine(home, ".vibe");

        var builder = ImmutableDictionary.CreateBuilder<string, AgentConfig>(StringComparer.Ordinal);

        builder.Add("aider-desk", new AgentConfig(
            Name: "aider-desk",
            DisplayName: "AiderDesk",
            SkillsDir: ".aider-desk/skills",
            GlobalSkillsDir: Path.Combine(home, ".aider-desk", "skills")));

        builder.Add("amp", new AgentConfig(
            Name: "amp",
            DisplayName: "Amp",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(configHome, "agents", "skills")));

        builder.Add("antigravity", new AgentConfig(
            Name: "antigravity",
            DisplayName: "Antigravity",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".gemini", "antigravity", "skills")));

        builder.Add("augment", new AgentConfig(
            Name: "augment",
            DisplayName: "Augment",
            SkillsDir: ".augment/skills",
            GlobalSkillsDir: Path.Combine(home, ".augment", "skills")));

        builder.Add("bob", new AgentConfig(
            Name: "bob",
            DisplayName: "IBM Bob",
            SkillsDir: ".bob/skills",
            GlobalSkillsDir: Path.Combine(home, ".bob", "skills")));

        builder.Add("claude-code", new AgentConfig(
            Name: "claude-code",
            DisplayName: "Claude Code",
            SkillsDir: ".claude/skills",
            GlobalSkillsDir: Path.Combine(claudeHome, "skills")));

        builder.Add("openclaw", new AgentConfig(
            Name: "openclaw",
            DisplayName: "OpenClaw",
            SkillsDir: "skills",
            GlobalSkillsDir: GetOpenClawGlobalSkillsDir(home, directoryExists)));

        builder.Add("cline", new AgentConfig(
            Name: "cline",
            DisplayName: "Cline",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".agents", "skills")));

        builder.Add("codearts-agent", new AgentConfig(
            Name: "codearts-agent",
            DisplayName: "CodeArts Agent",
            SkillsDir: ".codeartsdoer/skills",
            GlobalSkillsDir: Path.Combine(home, ".codeartsdoer", "skills")));

        builder.Add("codebuddy", new AgentConfig(
            Name: "codebuddy",
            DisplayName: "CodeBuddy",
            SkillsDir: ".codebuddy/skills",
            GlobalSkillsDir: Path.Combine(home, ".codebuddy", "skills")));

        builder.Add("codemaker", new AgentConfig(
            Name: "codemaker",
            DisplayName: "Codemaker",
            SkillsDir: ".codemaker/skills",
            GlobalSkillsDir: Path.Combine(home, ".codemaker", "skills")));

        builder.Add("codestudio", new AgentConfig(
            Name: "codestudio",
            DisplayName: "Code Studio",
            SkillsDir: ".codestudio/skills",
            GlobalSkillsDir: Path.Combine(home, ".codestudio", "skills")));

        builder.Add("codex", new AgentConfig(
            Name: "codex",
            DisplayName: "Codex",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(codexHome, "skills")));

        builder.Add("command-code", new AgentConfig(
            Name: "command-code",
            DisplayName: "Command Code",
            SkillsDir: ".commandcode/skills",
            GlobalSkillsDir: Path.Combine(home, ".commandcode", "skills")));

        builder.Add("continue", new AgentConfig(
            Name: "continue",
            DisplayName: "Continue",
            SkillsDir: ".continue/skills",
            GlobalSkillsDir: Path.Combine(home, ".continue", "skills")));

        builder.Add("cortex", new AgentConfig(
            Name: "cortex",
            DisplayName: "Cortex Code",
            SkillsDir: ".cortex/skills",
            GlobalSkillsDir: Path.Combine(home, ".snowflake", "cortex", "skills")));

        builder.Add("crush", new AgentConfig(
            Name: "crush",
            DisplayName: "Crush",
            SkillsDir: ".crush/skills",
            GlobalSkillsDir: Path.Combine(home, ".config", "crush", "skills")));

        builder.Add("cursor", new AgentConfig(
            Name: "cursor",
            DisplayName: "Cursor",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".cursor", "skills")));

        builder.Add("deepagents", new AgentConfig(
            Name: "deepagents",
            DisplayName: "Deep Agents",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".deepagents", "agent", "skills")));

        builder.Add("devin", new AgentConfig(
            Name: "devin",
            DisplayName: "Devin for Terminal",
            SkillsDir: ".devin/skills",
            GlobalSkillsDir: Path.Combine(configHome, "devin", "skills")));

        builder.Add("dexto", new AgentConfig(
            Name: "dexto",
            DisplayName: "Dexto",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".agents", "skills")));

        builder.Add("droid", new AgentConfig(
            Name: "droid",
            DisplayName: "Droid",
            SkillsDir: ".factory/skills",
            GlobalSkillsDir: Path.Combine(home, ".factory", "skills")));

        builder.Add("firebender", new AgentConfig(
            Name: "firebender",
            DisplayName: "Firebender",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".firebender", "skills")));

        builder.Add("forgecode", new AgentConfig(
            Name: "forgecode",
            DisplayName: "ForgeCode",
            SkillsDir: ".forge/skills",
            GlobalSkillsDir: Path.Combine(home, ".forge", "skills")));

        builder.Add("gemini-cli", new AgentConfig(
            Name: "gemini-cli",
            DisplayName: "Gemini CLI",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".gemini", "skills")));

        builder.Add("github-copilot", new AgentConfig(
            Name: "github-copilot",
            DisplayName: "GitHub Copilot",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".copilot", "skills")));

        builder.Add("goose", new AgentConfig(
            Name: "goose",
            DisplayName: "Goose",
            SkillsDir: ".goose/skills",
            GlobalSkillsDir: Path.Combine(configHome, "goose", "skills")));

        builder.Add("hermes-agent", new AgentConfig(
            Name: "hermes-agent",
            DisplayName: "Hermes Agent",
            SkillsDir: ".hermes/skills",
            GlobalSkillsDir: Path.Combine(home, ".hermes", "skills")));

        builder.Add("junie", new AgentConfig(
            Name: "junie",
            DisplayName: "Junie",
            SkillsDir: ".junie/skills",
            GlobalSkillsDir: Path.Combine(home, ".junie", "skills")));

        builder.Add("iflow-cli", new AgentConfig(
            Name: "iflow-cli",
            DisplayName: "iFlow CLI",
            SkillsDir: ".iflow/skills",
            GlobalSkillsDir: Path.Combine(home, ".iflow", "skills")));

        builder.Add("kilo", new AgentConfig(
            Name: "kilo",
            DisplayName: "Kilo Code",
            SkillsDir: ".kilocode/skills",
            GlobalSkillsDir: Path.Combine(home, ".kilocode", "skills")));

        builder.Add("kimi-cli", new AgentConfig(
            Name: "kimi-cli",
            DisplayName: "Kimi Code CLI",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".config", "agents", "skills")));

        builder.Add("kiro-cli", new AgentConfig(
            Name: "kiro-cli",
            DisplayName: "Kiro CLI",
            SkillsDir: ".kiro/skills",
            GlobalSkillsDir: Path.Combine(home, ".kiro", "skills")));

        builder.Add("kode", new AgentConfig(
            Name: "kode",
            DisplayName: "Kode",
            SkillsDir: ".kode/skills",
            GlobalSkillsDir: Path.Combine(home, ".kode", "skills")));

        builder.Add("mcpjam", new AgentConfig(
            Name: "mcpjam",
            DisplayName: "MCPJam",
            SkillsDir: ".mcpjam/skills",
            GlobalSkillsDir: Path.Combine(home, ".mcpjam", "skills")));

        builder.Add("mistral-vibe", new AgentConfig(
            Name: "mistral-vibe",
            DisplayName: "Mistral Vibe",
            SkillsDir: ".vibe/skills",
            GlobalSkillsDir: Path.Combine(vibeHome, "skills")));

        builder.Add("mux", new AgentConfig(
            Name: "mux",
            DisplayName: "Mux",
            SkillsDir: ".mux/skills",
            GlobalSkillsDir: Path.Combine(home, ".mux", "skills")));

        builder.Add("opencode", new AgentConfig(
            Name: "opencode",
            DisplayName: "OpenCode",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(configHome, "opencode", "skills")));

        builder.Add("openhands", new AgentConfig(
            Name: "openhands",
            DisplayName: "OpenHands",
            SkillsDir: ".openhands/skills",
            GlobalSkillsDir: Path.Combine(home, ".openhands", "skills")));

        builder.Add("pi", new AgentConfig(
            Name: "pi",
            DisplayName: "Pi",
            SkillsDir: ".pi/skills",
            GlobalSkillsDir: Path.Combine(home, ".pi", "agent", "skills")));

        builder.Add("qoder", new AgentConfig(
            Name: "qoder",
            DisplayName: "Qoder",
            SkillsDir: ".qoder/skills",
            GlobalSkillsDir: Path.Combine(home, ".qoder", "skills")));

        builder.Add("qwen-code", new AgentConfig(
            Name: "qwen-code",
            DisplayName: "Qwen Code",
            SkillsDir: ".qwen/skills",
            GlobalSkillsDir: Path.Combine(home, ".qwen", "skills")));

        builder.Add("replit", new AgentConfig(
            Name: "replit",
            DisplayName: "Replit",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(configHome, "agents", "skills"),
            ShowInUniversalList: false));

        builder.Add("rovodev", new AgentConfig(
            Name: "rovodev",
            DisplayName: "Rovo Dev",
            SkillsDir: ".rovodev/skills",
            GlobalSkillsDir: Path.Combine(home, ".rovodev", "skills")));

        builder.Add("roo", new AgentConfig(
            Name: "roo",
            DisplayName: "Roo Code",
            SkillsDir: ".roo/skills",
            GlobalSkillsDir: Path.Combine(home, ".roo", "skills")));

        builder.Add("tabnine-cli", new AgentConfig(
            Name: "tabnine-cli",
            DisplayName: "Tabnine CLI",
            SkillsDir: ".tabnine/agent/skills",
            GlobalSkillsDir: Path.Combine(home, ".tabnine", "agent", "skills")));

        builder.Add("trae", new AgentConfig(
            Name: "trae",
            DisplayName: "Trae",
            SkillsDir: ".trae/skills",
            GlobalSkillsDir: Path.Combine(home, ".trae", "skills")));

        builder.Add("trae-cn", new AgentConfig(
            Name: "trae-cn",
            DisplayName: "Trae CN",
            SkillsDir: ".trae/skills",
            GlobalSkillsDir: Path.Combine(home, ".trae-cn", "skills")));

        builder.Add("warp", new AgentConfig(
            Name: "warp",
            DisplayName: "Warp",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(home, ".agents", "skills")));

        builder.Add("windsurf", new AgentConfig(
            Name: "windsurf",
            DisplayName: "Windsurf",
            SkillsDir: ".windsurf/skills",
            GlobalSkillsDir: Path.Combine(home, ".codeium", "windsurf", "skills")));

        builder.Add("zencoder", new AgentConfig(
            Name: "zencoder",
            DisplayName: "Zencoder",
            SkillsDir: ".zencoder/skills",
            GlobalSkillsDir: Path.Combine(home, ".zencoder", "skills")));

        builder.Add("neovate", new AgentConfig(
            Name: "neovate",
            DisplayName: "Neovate",
            SkillsDir: ".neovate/skills",
            GlobalSkillsDir: Path.Combine(home, ".neovate", "skills")));

        builder.Add("pochi", new AgentConfig(
            Name: "pochi",
            DisplayName: "Pochi",
            SkillsDir: ".pochi/skills",
            GlobalSkillsDir: Path.Combine(home, ".pochi", "skills")));

        builder.Add("adal", new AgentConfig(
            Name: "adal",
            DisplayName: "AdaL",
            SkillsDir: ".adal/skills",
            GlobalSkillsDir: Path.Combine(home, ".adal", "skills")));

        builder.Add("universal", new AgentConfig(
            Name: "universal",
            DisplayName: "Universal",
            SkillsDir: ".agents/skills",
            GlobalSkillsDir: Path.Combine(configHome, "agents", "skills"),
            ShowInUniversalList: false));

        return builder.ToImmutable();
    }

    private static string? ResolveEnv(Func<string, string?> envReader, string name)
    {
        var value = envReader(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
