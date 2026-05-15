using System.Collections.Immutable;

namespace Skillz.Install;

internal sealed class AgentEnvironmentDetector : IAgentEnvironmentDetector
{
    private static readonly ImmutableDictionary<string, string> s_agentNameToType =
        new Dictionary<string, string>(StringComparer.Ordinal)
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
            Directory.GetCurrentDirectory)
    {
    }

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

    public Task<AgentDetectionResult> DetectAgentAsync()
    {
        if (_cachedResult is not null)
        {
            return Task.FromResult(_cachedResult);
        }

        var name = DetectAgentNameFromEnvironment();
        _cachedResult = new AgentDetectionResult(name is not null, name);
        return Task.FromResult(_cachedResult);
    }

    public async Task<bool> IsRunningInAgentAsync()
    {
        var result = await DetectAgentAsync().ConfigureAwait(false);
        return result.IsAgent;
    }

    public async Task<string?> GetAgentNameAsync()
    {
        var result = await DetectAgentAsync().ConfigureAwait(false);
        return result.IsAgent ? result.Name : null;
    }

    public string? GetAgentType(string agentName)
    {
        return s_agentNameToType.GetValueOrDefault(agentName);
    }

    public Task<IReadOnlyList<string>> DetectInstalledAgentsAsync()
    {
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
            _ => false,
        };
    }

    private string? DetectAgentNameFromEnvironment()
    {
        if (!string.IsNullOrEmpty(ResolveEnv("CURSOR_TRACE_ID"))
            || !string.IsNullOrEmpty(ResolveEnv("CURSOR_AGENT")))
        {
            return "cursor";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("CLAUDECODE"))
            || !string.IsNullOrEmpty(ResolveEnv("CLAUDE_CODE")))
        {
            return "claude";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("REPL_ID"))
            || !string.IsNullOrEmpty(ResolveEnv("REPLIT_DEV_DOMAIN")))
        {
            return "replit";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("GEMINI_CLI")))
        {
            return "gemini";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("CODEX_AGENT"))
            || !string.IsNullOrEmpty(ResolveEnv("CODEX_HOME")))
        {
            return "codex";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("OPENCODE")))
        {
            return "opencode";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("GITHUB_COPILOT_AGENT")))
        {
            return "github-copilot";
        }

        if (!string.IsNullOrEmpty(ResolveEnv("ANTIGRAVITY")))
        {
            return "antigravity";
        }

        return null;
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
