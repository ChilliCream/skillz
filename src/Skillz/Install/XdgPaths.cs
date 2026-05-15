namespace Skillz.Install;

internal interface IXdgPaths
{
    string GetConfigHome();

    string GetDataHome();

    string GetStateHome();

    string GetGlobalSkillsDir();

    string GetGlobalLockPath();

    string GetConfigDir();

    string GetLogDir();
}

internal sealed class XdgPaths : IXdgPaths
{
    private readonly string _home;
    private readonly Func<string, string?> _envReader;

    public XdgPaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable)
    {
    }

    public XdgPaths(string home, Func<string, string?> envReader)
    {
        _home = home;
        _envReader = envReader;
    }

    public string GetConfigHome()
    {
        var fromEnv = TrimToNull(_envReader("XDG_CONFIG_HOME"));
        return fromEnv ?? Path.Combine(_home, ".config");
    }

    public string GetDataHome()
    {
        var fromEnv = TrimToNull(_envReader("XDG_DATA_HOME"));
        return fromEnv ?? Path.Combine(_home, ".local", "share");
    }

    public string GetStateHome()
    {
        var fromEnv = TrimToNull(_envReader("XDG_STATE_HOME"));
        return fromEnv ?? Path.Combine(_home, ".local", "state");
    }

    public string GetGlobalSkillsDir()
    {
        return Path.Combine(GetDataHome(), "skillz", "skills");
    }

    public string GetGlobalLockPath()
    {
        var fromEnv = TrimToNull(_envReader("XDG_STATE_HOME"));
        if (fromEnv is not null)
        {
            return Path.Combine(fromEnv, "skills", ".skill-lock.json");
        }

        return Path.Combine(_home, ".agents", ".skill-lock.json");
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
