using System.Collections.Immutable;
using System.Linq;

namespace Skillz.Install;

internal sealed class AgentEnvironment(
    AgentRegistry registry,
    string home,
    Func<string, string?> envReader,
    Func<string, bool> directoryExists,
    Func<string> cwdProvider)
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

    public AgentEnvironment(AgentRegistry registry)
        : this(
            registry,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable,
            Directory.Exists,
            Directory.GetCurrentDirectory) { }

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
        var configHome = ResolveEnv("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        var codexHome = ResolveEnv("CODEX_HOME") ?? Path.Combine(home, ".codex");
        var claudeHome = ResolveEnv("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        var vibeHome = ResolveEnv("VIBE_HOME") ?? Path.Combine(home, ".vibe");

        return agentType switch
        {
            "aider-desk" => directoryExists(Path.Combine(home, ".aider-desk")),
            "amp" => directoryExists(Path.Combine(configHome, "amp")),
            "antigravity" => directoryExists(Path.Combine(home, ".gemini", "antigravity")),
            "augment" => directoryExists(Path.Combine(home, ".augment")),
            "bob" => directoryExists(Path.Combine(home, ".bob")),
            "claude-code" => directoryExists(claudeHome),
            "openclaw" => directoryExists(Path.Combine(home, ".openclaw"))
                || directoryExists(Path.Combine(home, ".clawdbot"))
                || directoryExists(Path.Combine(home, ".moltbot")),
            "cline" => directoryExists(Path.Combine(home, ".cline")),
            "codearts-agent" => directoryExists(Path.Combine(home, ".codeartsdoer")),
            "codebuddy" => directoryExists(Path.Combine(cwdProvider(), ".codebuddy"))
                || directoryExists(Path.Combine(home, ".codebuddy")),
            "codemaker" => directoryExists(Path.Combine(home, ".codemaker")),
            "codestudio" => directoryExists(Path.Combine(home, ".codestudio")),
            "codex" => directoryExists(codexHome) || directoryExists("/etc/codex"),
            "command-code" => directoryExists(Path.Combine(home, ".commandcode")),
            "continue" => directoryExists(Path.Combine(cwdProvider(), ".continue"))
                || directoryExists(Path.Combine(home, ".continue")),
            "cortex" => directoryExists(Path.Combine(home, ".snowflake", "cortex")),
            "crush" => directoryExists(Path.Combine(home, ".config", "crush")),
            "cursor" => directoryExists(Path.Combine(home, ".cursor")),
            "deepagents" => directoryExists(Path.Combine(home, ".deepagents")),
            "devin" => directoryExists(Path.Combine(configHome, "devin")),
            "dexto" => directoryExists(Path.Combine(home, ".dexto")),
            "droid" => directoryExists(Path.Combine(home, ".factory")),
            "firebender" => directoryExists(Path.Combine(home, ".firebender")),
            "forgecode" => directoryExists(Path.Combine(home, ".forge")),
            "gemini-cli" => directoryExists(Path.Combine(home, ".gemini")),
            "github-copilot" => directoryExists(Path.Combine(home, ".copilot")),
            "goose" => directoryExists(Path.Combine(configHome, "goose")),
            "hermes-agent" => directoryExists(Path.Combine(home, ".hermes")),
            "junie" => directoryExists(Path.Combine(home, ".junie")),
            "iflow-cli" => directoryExists(Path.Combine(home, ".iflow")),
            "kilo" => directoryExists(Path.Combine(home, ".kilocode")),
            "kimi-cli" => directoryExists(Path.Combine(home, ".kimi")),
            "kiro-cli" => directoryExists(Path.Combine(home, ".kiro")),
            "kode" => directoryExists(Path.Combine(home, ".kode")),
            "mcpjam" => directoryExists(Path.Combine(home, ".mcpjam")),
            "mistral-vibe" => directoryExists(vibeHome),
            "mux" => directoryExists(Path.Combine(home, ".mux")),
            "opencode" => directoryExists(Path.Combine(configHome, "opencode")),
            "openhands" => directoryExists(Path.Combine(home, ".openhands")),
            "pi" => directoryExists(Path.Combine(home, ".pi", "agent")),
            "qoder" => directoryExists(Path.Combine(home, ".qoder")),
            "qwen-code" => directoryExists(Path.Combine(home, ".qwen")),
            "replit" => directoryExists(Path.Combine(cwdProvider(), ".replit")),
            "rovodev" => directoryExists(Path.Combine(home, ".rovodev")),
            "roo" => directoryExists(Path.Combine(home, ".roo")),
            "tabnine-cli" => directoryExists(Path.Combine(home, ".tabnine")),
            "trae" => directoryExists(Path.Combine(home, ".trae")),
            "trae-cn" => directoryExists(Path.Combine(home, ".trae-cn")),
            "warp" => directoryExists(Path.Combine(home, ".warp")),
            "windsurf" => directoryExists(Path.Combine(home, ".codeium", "windsurf")),
            "zencoder" => directoryExists(Path.Combine(home, ".zencoder")),
            "neovate" => directoryExists(Path.Combine(home, ".neovate")),
            "pochi" => directoryExists(Path.Combine(home, ".pochi")),
            "adal" => directoryExists(Path.Combine(home, ".adal")),
            "universal" => false,
            _ => false
        };
    }

    private string? DetectAgentNameFromEnvironment()
    {
        // 1. Generic AI_AGENT — raw agent name from the environment, checked first
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
        if (directoryExists("/opt/.devin"))
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

        // Return raw value — FindAgentType will handle mapping or null
        return value;
    }

    private string? ResolveEnv(string name)
    {
        var value = envReader(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
