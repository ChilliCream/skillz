using Skillz.Install;
using Xunit;

namespace Skillz.Tests.Install;

public class XdgPathsTests
{
    private const string Home = "/home/test";

    private static XdgPaths Create(Dictionary<string, string?>? env = null)
    {
        env ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        return new XdgPaths(Home, name => env.GetValueOrDefault(name));
    }

    [Fact]
    public void GetConfigHome_NoEnv_FallsBackToDotConfig()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".config"), paths.GetConfigHome());
    }

    [Fact]
    public void GetConfigHome_HonorsXdgConfigHome()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = "/custom/config" };
        var paths = Create(env);
        Assert.Equal("/custom/config", paths.GetConfigHome());
    }

    [Fact]
    public void GetDataHome_NoEnv_FallsBackToLocalShare()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".local", "share"), paths.GetDataHome());
    }

    [Fact]
    public void GetDataHome_HonorsXdgDataHome()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_DATA_HOME"] = "/custom/data" };
        var paths = Create(env);
        Assert.Equal("/custom/data", paths.GetDataHome());
    }

    [Fact]
    public void GetStateHome_NoEnv_FallsBackToLocalState()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".local", "state"), paths.GetStateHome());
    }

    [Fact]
    public void GetStateHome_HonorsXdgStateHome()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_STATE_HOME"] = "/custom/state" };
        var paths = Create(env);
        Assert.Equal("/custom/state", paths.GetStateHome());
    }

    [Fact]
    public void GetGlobalLockPath_UsesXdgStateHomeWhenSet()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_STATE_HOME"] = "/custom/state" };
        var paths = Create(env);
        Assert.Equal(Path.Combine("/custom/state", "skills", ".skill-lock.json"), paths.GetGlobalLockPath());
    }

    [Fact]
    public void GetGlobalLockPath_FallsBackToDotAgentsWhenStateHomeNotSet()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".agents", ".skill-lock.json"), paths.GetGlobalLockPath());
    }

    [Fact]
    public void GetGlobalSkillsDir_UsesDataHome()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".local", "share", "skillz", "skills"), paths.GetGlobalSkillsDir());
    }

    [Fact]
    public void GetConfigDir_UsesConfigHome()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".config", "skillz"), paths.GetConfigDir());
    }

    [Fact]
    public void GetLogDir_UsesStateHome()
    {
        var paths = Create();
        Assert.Equal(Path.Combine(Home, ".local", "state", "skillz", "logs"), paths.GetLogDir());
    }

    [Fact]
    public void EmptyEnvValue_IsTreatedAsUnset()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = "",
            ["XDG_DATA_HOME"] = "   "
        };
        var paths = Create(env);

        Assert.Equal(Path.Combine(Home, ".config"), paths.GetConfigHome());
        Assert.Equal(Path.Combine(Home, ".local", "share"), paths.GetDataHome());
    }
}

public class XdgAgentPathsTests
{
    private const string Home = "/home/test";

    private static AgentRegistry CreateRegistry(Dictionary<string, string?>? env = null)
    {
        env ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        return new AgentRegistry(Home, name => env.GetValueOrDefault(name), _ => false);
    }

    [Fact]
    public void OpenCode_UsesDotConfigOpencodeSkills()
    {
        var registry = CreateRegistry();
        var config = registry.GetConfig("opencode");
        Assert.Equal(Path.Combine(Home, ".config", "opencode", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void OpenCode_DoesNotUsePlatformSpecificPaths()
    {
        var registry = CreateRegistry();
        var globalDir = registry.GetConfig("opencode").GlobalSkillsDir!;
        Assert.DoesNotContain("Library", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData", globalDir, StringComparison.Ordinal);
    }

    [Fact]
    public void Amp_UsesDotConfigAgentsSkills()
    {
        var registry = CreateRegistry();
        var config = registry.GetConfig("amp");
        Assert.Equal(Path.Combine(Home, ".config", "agents", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void Amp_DoesNotUsePlatformSpecificPaths()
    {
        var registry = CreateRegistry();
        var globalDir = registry.GetConfig("amp").GlobalSkillsDir!;
        Assert.DoesNotContain("Library", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData", globalDir, StringComparison.Ordinal);
    }

    [Fact]
    public void Goose_UsesDotConfigGooseSkills()
    {
        var registry = CreateRegistry();
        var config = registry.GetConfig("goose");
        Assert.Equal(Path.Combine(Home, ".config", "goose", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void Goose_DoesNotUsePlatformSpecificPaths()
    {
        var registry = CreateRegistry();
        var globalDir = registry.GetConfig("goose").GlobalSkillsDir!;
        Assert.DoesNotContain("Library", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData", globalDir, StringComparison.Ordinal);
    }

    [Fact]
    public void Cursor_UsesHomeBasedDotCursorSkills()
    {
        var registry = CreateRegistry();
        var config = registry.GetConfig("cursor");
        Assert.Equal(Path.Combine(Home, ".cursor", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void Cline_UsesHomeBasedDotAgentsSkills()
    {
        var registry = CreateRegistry();
        var config = registry.GetConfig("cline");
        Assert.Equal(Path.Combine(Home, ".agents", "skills"), config.GlobalSkillsDir);
    }

    [Fact]
    public void XdgConfigHome_OverridesDefaultsForXdgAgents()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = "/custom/xdg-config"
        };
        var registry = CreateRegistry(env);

        Assert.Equal(
            Path.Combine("/custom/xdg-config", "opencode", "skills"),
            registry.GetConfig("opencode").GlobalSkillsDir);
        Assert.Equal(
            Path.Combine("/custom/xdg-config", "goose", "skills"),
            registry.GetConfig("goose").GlobalSkillsDir);
        Assert.Equal(Path.Combine("/custom/xdg-config", "agents", "skills"), registry.GetConfig("amp").GlobalSkillsDir);
    }
}
