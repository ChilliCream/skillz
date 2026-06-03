using System.Collections.Immutable;

namespace Skillz.Install;

internal sealed class AgentRegistry(ISystemEnvironment system)
{
    public const string UniversalSkillsDir = ".agents/skills";

    public ImmutableDictionary<string, AgentConfig> All => field ??= BuildAgents(system);

    public ImmutableArray<string> AgentTypes
    {
        get
        {
            if (field.IsDefault)
            {
                field = All.Keys.ToImmutableArray();
            }

            return field;
        }
    }

    public ImmutableArray<string> UniversalAgents
    {
        get
        {
            if (field.IsDefault)
            {
                field = All.Where(kv => kv.Value.SkillsDirectory == UniversalSkillsDir && kv.Value.ShowInUniversalList)
                    .Select(kv => kv.Key)
                    .ToImmutableArray();
            }

            return field;
        }
    }

    public ImmutableArray<string> NonUniversalAgents
    {
        get
        {
            if (field.IsDefault)
            {
                field = All.Where(kv => kv.Value.SkillsDirectory != UniversalSkillsDir)
                    .Select(kv => kv.Key)
                    .ToImmutableArray();
            }

            return field;
        }
    }

    public AgentConfig GetConfig(string agentType)
    {
        if (!All.TryGetValue(agentType, out var config))
        {
            throw new ArgumentException($"Unknown agent type: {agentType}", nameof(agentType));
        }

        return config;
    }

    public bool TryGetConfig(string agentType, out AgentConfig? config)
    {
        if (All.TryGetValue(agentType, out var found))
        {
            config = found;
            return true;
        }

        config = null;
        return false;
    }

    public bool IsUniversalAgent(string agentType)
    {
        return All.TryGetValue(agentType, out var config) && config.SkillsDirectory == UniversalSkillsDir;
    }

    public static string GetOpenClawGlobalSkillsDir(ISystemEnvironment system)
    {
        var home = system.HomeDirectory;
        if (system.DirectoryExists(Path.Combine(home, ".openclaw")))
        {
            return Path.Combine(home, ".openclaw", "skills");
        }

        if (system.DirectoryExists(Path.Combine(home, ".clawdbot")))
        {
            return Path.Combine(home, ".clawdbot", "skills");
        }

        if (system.DirectoryExists(Path.Combine(home, ".moltbot")))
        {
            return Path.Combine(home, ".moltbot", "skills");
        }

        return Path.Combine(home, ".openclaw", "skills");
    }

    private static ImmutableDictionary<string, AgentConfig> BuildAgents(ISystemEnvironment system)
    {
        var home = system.HomeDirectory;
        var configHome = ResolveEnv(system, "XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        var codexHome = ResolveEnv(system, "CODEX_HOME") ?? Path.Combine(home, ".codex");
        var claudeHome = ResolveEnv(system, "CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        var vibeHome = ResolveEnv(system, "VIBE_HOME") ?? Path.Combine(home, ".vibe");

        var builder = ImmutableDictionary.CreateBuilder<string, AgentConfig>(StringComparer.Ordinal);

        builder.Add(
            "aider-desk",
            new AgentConfig(
                Name: "aider-desk",
                DisplayName: "AiderDesk",
                SkillsDirectory: ".aider-desk/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".aider-desk", "skills")));

        builder.Add(
            "amp",
            new AgentConfig(
                Name: "amp",
                DisplayName: "Amp",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(configHome, "agents", "skills")));

        builder.Add(
            "antigravity",
            new AgentConfig(
                Name: "antigravity",
                DisplayName: "Antigravity",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".gemini", "antigravity", "skills")));

        builder.Add(
            "augment",
            new AgentConfig(
                Name: "augment",
                DisplayName: "Augment",
                SkillsDirectory: ".augment/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".augment", "skills")));

        builder.Add(
            "bob",
            new AgentConfig(
                Name: "bob",
                DisplayName: "IBM Bob",
                SkillsDirectory: ".bob/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".bob", "skills")));

        builder.Add(
            "claude-code",
            new AgentConfig(
                Name: "claude-code",
                DisplayName: "Claude Code",
                SkillsDirectory: ".claude/skills",
                GlobalSkillsDirectory: Path.Combine(claudeHome, "skills")));

        builder.Add(
            "openclaw",
            new AgentConfig(
                Name: "openclaw",
                DisplayName: "OpenClaw",
                SkillsDirectory: "skills",
                GlobalSkillsDirectory: GetOpenClawGlobalSkillsDir(system)));

        builder.Add(
            "cline",
            new AgentConfig(
                Name: "cline",
                DisplayName: "Cline",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".agents", "skills")));

        builder.Add(
            "codearts-agent",
            new AgentConfig(
                Name: "codearts-agent",
                DisplayName: "CodeArts Agent",
                SkillsDirectory: ".codeartsdoer/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".codeartsdoer", "skills")));

        builder.Add(
            "codebuddy",
            new AgentConfig(
                Name: "codebuddy",
                DisplayName: "CodeBuddy",
                SkillsDirectory: ".codebuddy/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".codebuddy", "skills")));

        builder.Add(
            "codemaker",
            new AgentConfig(
                Name: "codemaker",
                DisplayName: "Codemaker",
                SkillsDirectory: ".codemaker/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".codemaker", "skills")));

        builder.Add(
            "codestudio",
            new AgentConfig(
                Name: "codestudio",
                DisplayName: "Code Studio",
                SkillsDirectory: ".codestudio/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".codestudio", "skills")));

        builder.Add(
            "codex",
            new AgentConfig(
                Name: "codex",
                DisplayName: "Codex",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(codexHome, "skills")));

        builder.Add(
            "command-code",
            new AgentConfig(
                Name: "command-code",
                DisplayName: "Command Code",
                SkillsDirectory: ".commandcode/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".commandcode", "skills")));

        builder.Add(
            "continue",
            new AgentConfig(
                Name: "continue",
                DisplayName: "Continue",
                SkillsDirectory: ".continue/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".continue", "skills")));

        builder.Add(
            "cortex",
            new AgentConfig(
                Name: "cortex",
                DisplayName: "Cortex Code",
                SkillsDirectory: ".cortex/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".snowflake", "cortex", "skills")));

        builder.Add(
            "crush",
            new AgentConfig(
                Name: "crush",
                DisplayName: "Crush",
                SkillsDirectory: ".crush/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".config", "crush", "skills")));

        builder.Add(
            "cursor",
            new AgentConfig(
                Name: "cursor",
                DisplayName: "Cursor",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".cursor", "skills")));

        builder.Add(
            "deepagents",
            new AgentConfig(
                Name: "deepagents",
                DisplayName: "Deep Agents",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".deepagents", "agent", "skills")));

        builder.Add(
            "devin",
            new AgentConfig(
                Name: "devin",
                DisplayName: "Devin for Terminal",
                SkillsDirectory: ".devin/skills",
                GlobalSkillsDirectory: Path.Combine(configHome, "devin", "skills")));

        builder.Add(
            "dexto",
            new AgentConfig(
                Name: "dexto",
                DisplayName: "Dexto",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".agents", "skills")));

        builder.Add(
            "droid",
            new AgentConfig(
                Name: "droid",
                DisplayName: "Droid",
                SkillsDirectory: ".factory/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".factory", "skills")));

        builder.Add(
            "firebender",
            new AgentConfig(
                Name: "firebender",
                DisplayName: "Firebender",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".firebender", "skills")));

        builder.Add(
            "forgecode",
            new AgentConfig(
                Name: "forgecode",
                DisplayName: "ForgeCode",
                SkillsDirectory: ".forge/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".forge", "skills")));

        builder.Add(
            "gemini-cli",
            new AgentConfig(
                Name: "gemini-cli",
                DisplayName: "Gemini CLI",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".gemini", "skills")));

        builder.Add(
            "github-copilot",
            new AgentConfig(
                Name: "github-copilot",
                DisplayName: "GitHub Copilot",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".copilot", "skills")));

        builder.Add(
            "goose",
            new AgentConfig(
                Name: "goose",
                DisplayName: "Goose",
                SkillsDirectory: ".goose/skills",
                GlobalSkillsDirectory: Path.Combine(configHome, "goose", "skills")));

        builder.Add(
            "hermes-agent",
            new AgentConfig(
                Name: "hermes-agent",
                DisplayName: "Hermes Agent",
                SkillsDirectory: ".hermes/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".hermes", "skills")));

        builder.Add(
            "junie",
            new AgentConfig(
                Name: "junie",
                DisplayName: "Junie",
                SkillsDirectory: ".junie/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".junie", "skills")));

        builder.Add(
            "iflow-cli",
            new AgentConfig(
                Name: "iflow-cli",
                DisplayName: "iFlow CLI",
                SkillsDirectory: ".iflow/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".iflow", "skills")));

        builder.Add(
            "kilo",
            new AgentConfig(
                Name: "kilo",
                DisplayName: "Kilo Code",
                SkillsDirectory: ".kilocode/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".kilocode", "skills")));

        builder.Add(
            "kimi-cli",
            new AgentConfig(
                Name: "kimi-cli",
                DisplayName: "Kimi Code CLI",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".config", "agents", "skills")));

        builder.Add(
            "kiro-cli",
            new AgentConfig(
                Name: "kiro-cli",
                DisplayName: "Kiro CLI",
                SkillsDirectory: ".kiro/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".kiro", "skills")));

        builder.Add(
            "kode",
            new AgentConfig(
                Name: "kode",
                DisplayName: "Kode",
                SkillsDirectory: ".kode/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".kode", "skills")));

        builder.Add(
            "mcpjam",
            new AgentConfig(
                Name: "mcpjam",
                DisplayName: "MCPJam",
                SkillsDirectory: ".mcpjam/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".mcpjam", "skills")));

        builder.Add(
            "mistral-vibe",
            new AgentConfig(
                Name: "mistral-vibe",
                DisplayName: "Mistral Vibe",
                SkillsDirectory: ".vibe/skills",
                GlobalSkillsDirectory: Path.Combine(vibeHome, "skills")));

        builder.Add(
            "mux",
            new AgentConfig(
                Name: "mux",
                DisplayName: "Mux",
                SkillsDirectory: ".mux/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".mux", "skills")));

        builder.Add(
            "opencode",
            new AgentConfig(
                Name: "opencode",
                DisplayName: "OpenCode",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(configHome, "opencode", "skills")));

        builder.Add(
            "openhands",
            new AgentConfig(
                Name: "openhands",
                DisplayName: "OpenHands",
                SkillsDirectory: ".openhands/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".openhands", "skills")));

        builder.Add(
            "pi",
            new AgentConfig(
                Name: "pi",
                DisplayName: "Pi",
                SkillsDirectory: ".pi/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".pi", "agent", "skills")));

        builder.Add(
            "qoder",
            new AgentConfig(
                Name: "qoder",
                DisplayName: "Qoder",
                SkillsDirectory: ".qoder/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".qoder", "skills")));

        builder.Add(
            "qwen-code",
            new AgentConfig(
                Name: "qwen-code",
                DisplayName: "Qwen Code",
                SkillsDirectory: ".qwen/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".qwen", "skills")));

        builder.Add(
            "replit",
            new AgentConfig(
                Name: "replit",
                DisplayName: "Replit",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(configHome, "agents", "skills"),
                ShowInUniversalList: false));

        builder.Add(
            "rovodev",
            new AgentConfig(
                Name: "rovodev",
                DisplayName: "Rovo Dev",
                SkillsDirectory: ".rovodev/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".rovodev", "skills")));

        builder.Add(
            "roo",
            new AgentConfig(
                Name: "roo",
                DisplayName: "Roo Code",
                SkillsDirectory: ".roo/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".roo", "skills")));

        builder.Add(
            "tabnine-cli",
            new AgentConfig(
                Name: "tabnine-cli",
                DisplayName: "Tabnine CLI",
                SkillsDirectory: ".tabnine/agent/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".tabnine", "agent", "skills")));

        builder.Add(
            "trae",
            new AgentConfig(
                Name: "trae",
                DisplayName: "Trae",
                SkillsDirectory: ".trae/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".trae", "skills")));

        builder.Add(
            "trae-cn",
            new AgentConfig(
                Name: "trae-cn",
                DisplayName: "Trae CN",
                SkillsDirectory: ".trae/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".trae-cn", "skills")));

        builder.Add(
            "warp",
            new AgentConfig(
                Name: "warp",
                DisplayName: "Warp",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".agents", "skills")));

        builder.Add(
            "windsurf",
            new AgentConfig(
                Name: "windsurf",
                DisplayName: "Windsurf",
                SkillsDirectory: ".windsurf/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".codeium", "windsurf", "skills")));

        builder.Add(
            "zencoder",
            new AgentConfig(
                Name: "zencoder",
                DisplayName: "Zencoder",
                SkillsDirectory: ".zencoder/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".zencoder", "skills")));

        builder.Add(
            "neovate",
            new AgentConfig(
                Name: "neovate",
                DisplayName: "Neovate",
                SkillsDirectory: ".neovate/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".neovate", "skills")));

        builder.Add(
            "pochi",
            new AgentConfig(
                Name: "pochi",
                DisplayName: "Pochi",
                SkillsDirectory: ".pochi/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".pochi", "skills")));

        builder.Add(
            "adal",
            new AgentConfig(
                Name: "adal",
                DisplayName: "AdaL",
                SkillsDirectory: ".adal/skills",
                GlobalSkillsDirectory: Path.Combine(home, ".adal", "skills")));

        builder.Add(
            "universal",
            new AgentConfig(
                Name: "universal",
                DisplayName: "Universal",
                SkillsDirectory: ".agents/skills",
                GlobalSkillsDirectory: Path.Combine(configHome, "agents", "skills"),
                ShowInUniversalList: false));

        return builder.ToImmutable();
    }

    private static string? ResolveEnv(ISystemEnvironment system, string name)
    {
        var value = system.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
