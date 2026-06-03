namespace Skillz.Install;

internal sealed class XdgPaths(ISystemEnvironment system)
{
    public string GetConfigHome()
    {
        var fromEnv = TrimToNull(system.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        return fromEnv ?? Path.Combine(system.HomeDirectory, ".config");
    }

    public string GetDataHome()
    {
        var fromEnv = TrimToNull(system.GetEnvironmentVariable("XDG_DATA_HOME"));
        return fromEnv ?? Path.Combine(system.HomeDirectory, ".local", "share");
    }

    public string GetStateHome()
    {
        var fromEnv = TrimToNull(system.GetEnvironmentVariable("XDG_STATE_HOME"));
        return fromEnv ?? Path.Combine(system.HomeDirectory, ".local", "state");
    }

    public string GetGlobalSkillsDirectory()
    {
        return Path.Combine(GetDataHome(), "skillz", "skills");
    }

    public string GetGlobalLockPath()
    {
        var fromEnv = TrimToNull(system.GetEnvironmentVariable("XDG_STATE_HOME"));
        if (fromEnv is not null)
        {
            return Path.Combine(fromEnv, "skills", ".skill-lock.json");
        }

        return Path.Combine(system.HomeDirectory, ".agents", ".skill-lock.json");
    }

    public string GetConfigDirectory()
    {
        return Path.Combine(GetConfigHome(), "skillz");
    }

    public string GetLogDirectory()
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
