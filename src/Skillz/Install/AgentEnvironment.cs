using System.Collections.Immutable;

namespace Skillz.Install;

internal sealed class AgentEnvironment(AgentRegistry registry, ISystemEnvironment system)
{
    private static readonly ImmutableDictionary<string, string> s_agentNameToType = new Dictionary<string, string>
    {
        ["cursor"] = "cursor",
        ["cursor-cli"] = "cursor",
        ["claude"] = "claude-code",
        ["cowork"] = "claude-code",
        ["devin"] = "universal",
        ["replit"] = "replit",
        ["gemini"] = "gemini-cli",
        ["codex"] = "codex",
        ["antigravity"] = "antigravity",
        ["augment-cli"] = "augment",
        ["opencode"] = "opencode",
        ["github-copilot"] = "github-copilot"
    }.ToImmutableDictionary(StringComparer.Ordinal);

    public string? CurrentAgentName => field ??= DetectAgentNameFromEnvironment();

    public bool IsRunningInsideAgent => CurrentAgentName is not null;

    public string? FindAgentType(string agentName) => s_agentNameToType.GetValueOrDefault(agentName);

    public ImmutableArray<string> InstalledAgents
    {
        get
        {
            if (field.IsDefault)
            {
                field = [.. registry.All.Keys.Where(IsInstalled)];
            }

            return field;
        }
    }

    private bool IsInstalled(string agentType)
    {
        var home = system.HomeDirectory;
        var configHome = ResolveEnv("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        var codexHome = ResolveEnv("CODEX_HOME") ?? Path.Combine(home, ".codex");
        var claudeHome = ResolveEnv("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        var vibeHome = ResolveEnv("VIBE_HOME") ?? Path.Combine(home, ".vibe");

        return agentType switch
        {
            "aider-desk" => system.DirectoryExists(Path.Combine(home, ".aider-desk")),
            "amp" => system.DirectoryExists(Path.Combine(configHome, "amp")),
            "antigravity" => system.DirectoryExists(Path.Combine(home, ".gemini", "antigravity")),
            "augment" => system.DirectoryExists(Path.Combine(home, ".augment")),
            "bob" => system.DirectoryExists(Path.Combine(home, ".bob")),
            "claude-code" => system.DirectoryExists(claudeHome),
            "openclaw" => system.DirectoryExists(Path.Combine(home, ".openclaw"))
                || system.DirectoryExists(Path.Combine(home, ".clawdbot"))
                || system.DirectoryExists(Path.Combine(home, ".moltbot")),
            "cline" => system.DirectoryExists(Path.Combine(home, ".cline")),
            "codearts-agent" => system.DirectoryExists(Path.Combine(home, ".codeartsdoer")),
            "codebuddy" => system.DirectoryExists(Path.Combine(system.CurrentDirectory, ".codebuddy"))
                || system.DirectoryExists(Path.Combine(home, ".codebuddy")),
            "codemaker" => system.DirectoryExists(Path.Combine(home, ".codemaker")),
            "codestudio" => system.DirectoryExists(Path.Combine(home, ".codestudio")),
            "codex" => system.DirectoryExists(codexHome) || system.DirectoryExists("/etc/codex"),
            "command-code" => system.DirectoryExists(Path.Combine(home, ".commandcode")),
            "continue" => system.DirectoryExists(Path.Combine(system.CurrentDirectory, ".continue"))
                || system.DirectoryExists(Path.Combine(home, ".continue")),
            "cortex" => system.DirectoryExists(Path.Combine(home, ".snowflake", "cortex")),
            "crush" => system.DirectoryExists(Path.Combine(home, ".config", "crush")),
            "cursor" => system.DirectoryExists(Path.Combine(home, ".cursor")),
            "deepagents" => system.DirectoryExists(Path.Combine(home, ".deepagents")),
            "devin" => system.DirectoryExists(Path.Combine(configHome, "devin")),
            "dexto" => system.DirectoryExists(Path.Combine(home, ".dexto")),
            "droid" => system.DirectoryExists(Path.Combine(home, ".factory")),
            "firebender" => system.DirectoryExists(Path.Combine(home, ".firebender")),
            "forgecode" => system.DirectoryExists(Path.Combine(home, ".forge")),
            "gemini-cli" => system.DirectoryExists(Path.Combine(home, ".gemini")),
            "github-copilot" => system.DirectoryExists(Path.Combine(home, ".copilot")),
            "goose" => system.DirectoryExists(Path.Combine(configHome, "goose")),
            "hermes-agent" => system.DirectoryExists(Path.Combine(home, ".hermes")),
            "junie" => system.DirectoryExists(Path.Combine(home, ".junie")),
            "iflow-cli" => system.DirectoryExists(Path.Combine(home, ".iflow")),
            "kilo" => system.DirectoryExists(Path.Combine(home, ".kilocode")),
            "kimi-cli" => system.DirectoryExists(Path.Combine(home, ".kimi")),
            "kiro-cli" => system.DirectoryExists(Path.Combine(home, ".kiro")),
            "kode" => system.DirectoryExists(Path.Combine(home, ".kode")),
            "mcpjam" => system.DirectoryExists(Path.Combine(home, ".mcpjam")),
            "mistral-vibe" => system.DirectoryExists(vibeHome),
            "mux" => system.DirectoryExists(Path.Combine(home, ".mux")),
            "opencode" => system.DirectoryExists(Path.Combine(configHome, "opencode")),
            "openhands" => system.DirectoryExists(Path.Combine(home, ".openhands")),
            "pi" => system.DirectoryExists(Path.Combine(home, ".pi", "agent")),
            "qoder" => system.DirectoryExists(Path.Combine(home, ".qoder")),
            "qwen-code" => system.DirectoryExists(Path.Combine(home, ".qwen")),
            "replit" => system.DirectoryExists(Path.Combine(system.CurrentDirectory, ".replit")),
            "rovodev" => system.DirectoryExists(Path.Combine(home, ".rovodev")),
            "roo" => system.DirectoryExists(Path.Combine(home, ".roo")),
            "tabnine-cli" => system.DirectoryExists(Path.Combine(home, ".tabnine")),
            "trae" => system.DirectoryExists(Path.Combine(home, ".trae")),
            "trae-cn" => system.DirectoryExists(Path.Combine(home, ".trae-cn")),
            "warp" => system.DirectoryExists(Path.Combine(home, ".warp")),
            "windsurf" => system.DirectoryExists(Path.Combine(home, ".codeium", "windsurf")),
            "zencoder" => system.DirectoryExists(Path.Combine(home, ".zencoder")),
            "neovate" => system.DirectoryExists(Path.Combine(home, ".neovate")),
            "pochi" => system.DirectoryExists(Path.Combine(home, ".pochi")),
            "adal" => system.DirectoryExists(Path.Combine(home, ".adal")),
            "universal" => false,
            _ => false
        };
    }

    private string? DetectAgentNameFromEnvironment()
    {
        // 1. Generic AI_AGENT - raw agent name from the environment, checked first
        var aiAgent = ResolveEnv("AI_AGENT");
        if (!string.IsNullOrEmpty(aiAgent))
        {
            // AI_AGENT may contain version suffix like "claude-code_2-1-143_agent"
            // Extract just the agent name portion (before first underscore-digit)
            var mapped = MapAiAgentValue(aiAgent);
            if (mapped is not null)
            {
                return mapped;
            }
        }

        // 2. Cursor
        if (!string.IsNullOrEmpty(ResolveEnv("CURSOR_TRACE_ID")) || !string.IsNullOrEmpty(ResolveEnv("CURSOR_AGENT")))
        {
            return "cursor";
        }

        // 3. Claude Code
        if (!string.IsNullOrEmpty(ResolveEnv("CLAUDECODE")) || !string.IsNullOrEmpty(ResolveEnv("CLAUDE_CODE")))
        {
            return "claude";
        }

        // 4. Claude Code cowork mode
        if (!string.IsNullOrEmpty(ResolveEnv("CLAUDE_CODE_IS_COWORK")))
        {
            return "cowork";
        }

        // 5. Codex
        if (!string.IsNullOrEmpty(ResolveEnv("CODEX_SANDBOX"))
            || !string.IsNullOrEmpty(ResolveEnv("CODEX_CI"))
            || !string.IsNullOrEmpty(ResolveEnv("CODEX_THREAD_ID")))
        {
            return "codex";
        }

        // 6. Replit (REPL_ID or REPLIT_DEV_DOMAIN)
        if (!string.IsNullOrEmpty(ResolveEnv("REPL_ID")) || !string.IsNullOrEmpty(ResolveEnv("REPLIT_DEV_DOMAIN")))
        {
            return "replit";
        }

        // 7. Gemini
        if (!string.IsNullOrEmpty(ResolveEnv("GEMINI_CLI")))
        {
            return "gemini";
        }

        // 8. OpenCode
        if (!string.IsNullOrEmpty(ResolveEnv("OPENCODE_CLIENT")))
        {
            return "opencode";
        }

        // 9. GitHub Copilot
        if (!string.IsNullOrEmpty(ResolveEnv("COPILOT_MODEL"))
            || !string.IsNullOrEmpty(ResolveEnv("COPILOT_ALLOW_ALL"))
            || !string.IsNullOrEmpty(ResolveEnv("COPILOT_GITHUB_TOKEN")))
        {
            return "github-copilot";
        }

        // 10. Antigravity
        if (!string.IsNullOrEmpty(ResolveEnv("ANTIGRAVITY_AGENT")))
        {
            return "antigravity";
        }

        // 11. Augment
        if (!string.IsNullOrEmpty(ResolveEnv("AUGMENT_AGENT")))
        {
            return "augment-cli";
        }

        // 12. Devin (filesystem probe)
        if (system.DirectoryExists("/opt/.devin"))
        {
            return "devin";
        }

        return null;
    }

    private static string? MapAiAgentValue(string value)
    {
        // AI_AGENT values can be raw names like "claude-code" or versioned like "claude-code_2-1-143_agent"
        // Try exact match first, then try prefix before first underscore-followed-by-digit
        if (s_agentNameToType.ContainsKey(value))
        {
            return value;
        }

        // Try stripping version suffix: find first "_" followed by digit
        for (var i = 0; i < value.Length - 1; i++)
        {
            if (value[i] == '_' && char.IsAsciiDigit(value[i + 1]))
            {
                var prefix = value[..i];
                if (s_agentNameToType.ContainsKey(prefix))
                {
                    return prefix;
                }
                break;
            }
        }

        // Return raw value - FindAgentType will handle mapping or null
        return value;
    }

    private string? ResolveEnv(string name)
    {
        var value = system.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
