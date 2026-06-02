namespace Skillz.Install;

internal sealed class XdgPaths(string home, Func<string, string?> envReader)
{
    public XdgPaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable) { }

    public string GetConfigHome()
    {
        var fromEnv = TrimToNull(envReader("XDG_CONFIG_HOME"));
        return fromEnv ?? Path.Combine(home, ".config");
    }

    public string GetDataHome()
    {
        var fromEnv = TrimToNull(envReader("XDG_DATA_HOME"));
        return fromEnv ?? Path.Combine(home, ".local", "share");
    }

    public string GetStateHome()
    {
        var fromEnv = TrimToNull(envReader("XDG_STATE_HOME"));
        return fromEnv ?? Path.Combine(home, ".local", "state");
    }

    public string GetGlobalSkillsDir()
    {
        return Path.Combine(GetDataHome(), "skillz", "skills");
    }

    public string GetGlobalLockPath()
    {
        var fromEnv = TrimToNull(envReader("XDG_STATE_HOME"));
        if (fromEnv is not null)
        {
            return Path.Combine(fromEnv, "skills", ".skill-lock.json");
        }

        return Path.Combine(home, ".agents", ".skill-lock.json");
    }

    public string GetConfigDir()
    {
        return Path.Combine(GetConfigHome(), "skillz");
    }

    public string GetLogDir()
    {
        return Path.Combine(GetStateHome(), "skillz", "logs");
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
