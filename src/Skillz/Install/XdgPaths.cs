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
        return Path.Combine(GetGlobalRoot(), "skills");
    }

    /// <summary>
    /// The global lock file lives alongside the global skills directory, under the same
    /// <c>&lt;data-home&gt;/skillz</c> root, so the lock always describes where skills actually live.
    /// </summary>
    public string GetGlobalLockPath()
    {
        return Path.Combine(GetGlobalRoot(), ".skill-lock.json");
    }

    private string GetGlobalRoot()
    {
        return Path.Combine(GetDataHome(), "skillz");
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
