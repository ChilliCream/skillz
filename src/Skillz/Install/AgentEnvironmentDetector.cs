using System.Collections.Immutable;

namespace Skillz.Install;

internal sealed class AgentEnvironmentDetector : IAgentEnvironmentDetector
{
    private static readonly ImmutableDictionary<string, string> s_agentNameToType = new Dictionary<string, string>(
        StringComparer.Ordinal)
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

    private readonly IAgentRegistry _registry;
    private readonly string _home;
    private readonly Func<string, string?> _envReader;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string> _cwdProvider;
    private AgentDetectionResult? _cachedResult;

    public AgentEnvironmentDetector(IAgentRegistry registry)
        : this(
            registry,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable,
            Directory.Exists,
            Directory.GetCurrentDirectory) { }

    public AgentEnvironmentDetector(
        IAgentRegistry registry,
        string home,
        Func<string, string?> envReader,
        Func<string, bool> directoryExists,
        Func<string> cwdProvider)
    {
        _registry = registry;
        _home = home;
        _envReader = envReader;
        _directoryExists = directoryExists;
        _cwdProvider = cwdProvider;
    }

    public Task<AgentDetectionResult> DetectAgentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cachedResult is not null)
        {
            return Task.FromResult(_cachedResult);
        }

        var name = DetectAgentNameFromEnvironment();
        _cachedResult = new AgentDetectionResult(name is not null, name);
        return Task.FromResult(_cachedResult);
    }

    public async Task<bool> IsRunningInAgentAsync(CancellationToken cancellationToken = default)
    {
        var result = await DetectAgentAsync(cancellationToken);
        return result.IsAgent;
    }

    public async Task<string?> GetAgentNameAsync(CancellationToken cancellationToken = default)
    {
        var result = await DetectAgentAsync(cancellationToken);
        return result.IsAgent ? result.Name : null;
    }

    public string? GetAgentType(string agentName)
    {
        return s_agentNameToType.GetValueOrDefault(agentName);
    }

    public Task<IReadOnlyList<string>> DetectInstalledAgentsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installed = new List<string>();
        foreach (var (type, _) in _registry.All)
        {
            if (IsInstalled(type))
            {
                installed.Add(type);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(installed);
    }

    private bool IsInstalled(string agentType)
    {
        var configHome = ResolveEnv("XDG_CONFIG_HOME") ?? Path.Combine(_home, ".config");
        var codexHome = ResolveEnv("CODEX_HOME") ?? Path.Combine(_home, ".codex");
        var claudeHome = ResolveEnv("CLAUDE_CONFIG_DIR") ?? Path.Combine(_home, ".claude");
        var vibeHome = ResolveEnv("VIBE_HOME") ?? Path.Combine(_home, ".vibe");

        return agentType switch
        {
            "aider-desk" => _directoryExists(Path.Combine(_home, ".aider-desk")),
            "amp" => _directoryExists(Path.Combine(configHome, "amp")),
            "antigravity" => _directoryExists(Path.Combine(_home, ".gemini", "antigravity")),
            "augment" => _directoryExists(Path.Combine(_home, ".augment")),
            "bob" => _directoryExists(Path.Combine(_home, ".bob")),
            "claude-code" => _directoryExists(claudeHome),
            "openclaw" => _directoryExists(Path.Combine(_home, ".openclaw"))
                || _directoryExists(Path.Combine(_home, ".clawdbot"))
                || _directoryExists(Path.Combine(_home, ".moltbot")),
            "cline" => _directoryExists(Path.Combine(_home, ".cline")),
            "codearts-agent" => _directoryExists(Path.Combine(_home, ".codeartsdoer")),
            "codebuddy" => _directoryExists(Path.Combine(_cwdProvider(), ".codebuddy"))
                || _directoryExists(Path.Combine(_home, ".codebuddy")),
            "codemaker" => _directoryExists(Path.Combine(_home, ".codemaker")),
            "codestudio" => _directoryExists(Path.Combine(_home, ".codestudio")),
            "codex" => _directoryExists(codexHome) || _directoryExists("/etc/codex"),
            "command-code" => _directoryExists(Path.Combine(_home, ".commandcode")),
            "continue" => _directoryExists(Path.Combine(_cwdProvider(), ".continue"))
                || _directoryExists(Path.Combine(_home, ".continue")),
            "cortex" => _directoryExists(Path.Combine(_home, ".snowflake", "cortex")),
            "crush" => _directoryExists(Path.Combine(_home, ".config", "crush")),
            "cursor" => _directoryExists(Path.Combine(_home, ".cursor")),
            "deepagents" => _directoryExists(Path.Combine(_home, ".deepagents")),
            "devin" => _directoryExists(Path.Combine(configHome, "devin")),
            "dexto" => _directoryExists(Path.Combine(_home, ".dexto")),
            "droid" => _directoryExists(Path.Combine(_home, ".factory")),
            "firebender" => _directoryExists(Path.Combine(_home, ".firebender")),
            "forgecode" => _directoryExists(Path.Combine(_home, ".forge")),
            "gemini-cli" => _directoryExists(Path.Combine(_home, ".gemini")),
            "github-copilot" => _directoryExists(Path.Combine(_home, ".copilot")),
            "goose" => _directoryExists(Path.Combine(configHome, "goose")),
            "hermes-agent" => _directoryExists(Path.Combine(_home, ".hermes")),
            "junie" => _directoryExists(Path.Combine(_home, ".junie")),
            "iflow-cli" => _directoryExists(Path.Combine(_home, ".iflow")),
            "kilo" => _directoryExists(Path.Combine(_home, ".kilocode")),
            "kimi-cli" => _directoryExists(Path.Combine(_home, ".kimi")),
            "kiro-cli" => _directoryExists(Path.Combine(_home, ".kiro")),
            "kode" => _directoryExists(Path.Combine(_home, ".kode")),
            "mcpjam" => _directoryExists(Path.Combine(_home, ".mcpjam")),
            "mistral-vibe" => _directoryExists(vibeHome),
            "mux" => _directoryExists(Path.Combine(_home, ".mux")),
            "opencode" => _directoryExists(Path.Combine(configHome, "opencode")),
            "openhands" => _directoryExists(Path.Combine(_home, ".openhands")),
            "pi" => _directoryExists(Path.Combine(_home, ".pi", "agent")),
            "qoder" => _directoryExists(Path.Combine(_home, ".qoder")),
            "qwen-code" => _directoryExists(Path.Combine(_home, ".qwen")),
            "replit" => _directoryExists(Path.Combine(_cwdProvider(), ".replit")),
            "rovodev" => _directoryExists(Path.Combine(_home, ".rovodev")),
            "roo" => _directoryExists(Path.Combine(_home, ".roo")),
            "tabnine-cli" => _directoryExists(Path.Combine(_home, ".tabnine")),
            "trae" => _directoryExists(Path.Combine(_home, ".trae")),
            "trae-cn" => _directoryExists(Path.Combine(_home, ".trae-cn")),
            "warp" => _directoryExists(Path.Combine(_home, ".warp")),
            "windsurf" => _directoryExists(Path.Combine(_home, ".codeium", "windsurf")),
            "zencoder" => _directoryExists(Path.Combine(_home, ".zencoder")),
            "neovate" => _directoryExists(Path.Combine(_home, ".neovate")),
            "pochi" => _directoryExists(Path.Combine(_home, ".pochi")),
            "adal" => _directoryExists(Path.Combine(_home, ".adal")),
            "universal" => false,
            _ => false
        };
    }

    private string? DetectAgentNameFromEnvironment()
    {
        // 1. Generic AI_AGENT (TS checks this first — raw agent name from the environment)
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

        // 6. Replit (skz keeps REPLIT_DEV_DOMAIN — our extension beyond TS)
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
        if (_directoryExists("/opt/.devin"))
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

        // Return raw value — GetAgentType will handle mapping or null
        return value;
    }

    private string? ResolveEnv(string name)
    {
        var value = _envReader(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
