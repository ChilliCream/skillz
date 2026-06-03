using Skillz.Install;
using Skillz.Tests.TestServices;
using Xunit;

namespace Skillz.Tests.Install;

public class XdgPathsTests
{
    private const string Home = "/home/test";

    private static XdgPaths Create(Dictionary<string, string?>? env = null)
    {
        env ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        return new XdgPaths(new FakeSystemEnvironment { HomeDirectory = Home, Env = env });
    }

    [Fact]
    public void GetConfigHome_NoEnv_FallsBackToDotConfig()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".config"), paths.GetConfigHome());
    }

    [Fact]
    public void GetConfigHome_HonorsXdgConfigHome()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = "/custom/config" };
        var paths = Create(env);

        // Act & Assert
        Assert.Equal("/custom/config", paths.GetConfigHome());
    }

    [Fact]
    public void GetDataHome_NoEnv_FallsBackToLocalShare()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".local", "share"), paths.GetDataHome());
    }

    [Fact]
    public void GetDataHome_HonorsXdgDataHome()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_DATA_HOME"] = "/custom/data" };
        var paths = Create(env);

        // Act & Assert
        Assert.Equal("/custom/data", paths.GetDataHome());
    }

    [Fact]
    public void GetStateHome_NoEnv_FallsBackToLocalState()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".local", "state"), paths.GetStateHome());
    }

    [Fact]
    public void GetStateHome_HonorsXdgStateHome()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_STATE_HOME"] = "/custom/state" };
        var paths = Create(env);

        // Act & Assert
        Assert.Equal("/custom/state", paths.GetStateHome());
    }

    [Fact]
    public void GetGlobalLockPath_UsesXdgStateHomeWhenSet()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_STATE_HOME"] = "/custom/state" };
        var paths = Create(env);

        // Act & Assert
        Assert.Equal(Path.Combine("/custom/state", "skills", ".skill-lock.json"), paths.GetGlobalLockPath());
    }

    [Fact]
    public void GetGlobalLockPath_FallsBackToDotAgentsWhenStateHomeNotSet()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".agents", ".skill-lock.json"), paths.GetGlobalLockPath());
    }

    [Fact]
    public void GetGlobalSkillsDir_UsesDataHome()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".local", "share", "skillz", "skills"), paths.GetGlobalSkillsDirectory());
    }

    [Fact]
    public void GetConfigDir_UsesConfigHome()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".config", "skillz"), paths.GetConfigDirectory());
    }

    [Fact]
    public void GetLogDir_UsesStateHome()
    {
        // Arrange
        var paths = Create();

        // Act & Assert
        Assert.Equal(Path.Combine(Home, ".local", "state", "skillz", "logs"), paths.GetLogDirectory());
    }

    [Fact]
    public void EmptyEnvValue_IsTreatedAsUnset()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = "",
            ["XDG_DATA_HOME"] = "   "
        };
        var paths = Create(env);

        // Act & Assert
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
        return new AgentRegistry(new FakeSystemEnvironment { HomeDirectory = Home, Env = env });
    }

    [Fact]
    public void OpenCode_UsesDotConfigOpencodeSkills()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("opencode");

        // Assert
        Assert.Equal(Path.Combine(Home, ".config", "opencode", "skills"), config.GlobalSkillsDirectory);
    }

    [Fact]
    public void OpenCode_DoesNotUsePlatformSpecificPaths()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var globalDir = registry.GetConfig("opencode").GlobalSkillsDirectory!;

        // Assert
        Assert.DoesNotContain("Library", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData", globalDir, StringComparison.Ordinal);
    }

    [Fact]
    public void Amp_UsesDotConfigAgentsSkills()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("amp");

        // Assert
        Assert.Equal(Path.Combine(Home, ".config", "agents", "skills"), config.GlobalSkillsDirectory);
    }

    [Fact]
    public void Amp_DoesNotUsePlatformSpecificPaths()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var globalDir = registry.GetConfig("amp").GlobalSkillsDirectory!;

        // Assert
        Assert.DoesNotContain("Library", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData", globalDir, StringComparison.Ordinal);
    }

    [Fact]
    public void Goose_UsesDotConfigGooseSkills()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("goose");

        // Assert
        Assert.Equal(Path.Combine(Home, ".config", "goose", "skills"), config.GlobalSkillsDirectory);
    }

    [Fact]
    public void Goose_DoesNotUsePlatformSpecificPaths()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var globalDir = registry.GetConfig("goose").GlobalSkillsDirectory!;

        // Assert
        Assert.DoesNotContain("Library", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences", globalDir, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData", globalDir, StringComparison.Ordinal);
    }

    [Fact]
    public void Cursor_UsesHomeBasedDotCursorSkills()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("cursor");

        // Assert
        Assert.Equal(Path.Combine(Home, ".cursor", "skills"), config.GlobalSkillsDirectory);
    }

    [Fact]
    public void Cline_UsesHomeBasedDotAgentsSkills()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var config = registry.GetConfig("cline");

        // Assert
        Assert.Equal(Path.Combine(Home, ".agents", "skills"), config.GlobalSkillsDirectory);
    }

    [Fact]
    public void XdgConfigHome_OverridesDefaultsForXdgAgents()
    {
        // Arrange
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = "/custom/xdg-config"
        };
        var registry = CreateRegistry(env);

        // Act & Assert
        Assert.Equal(
            Path.Combine("/custom/xdg-config", "opencode", "skills"),
            registry.GetConfig("opencode").GlobalSkillsDirectory);
        Assert.Equal(
            Path.Combine("/custom/xdg-config", "goose", "skills"),
            registry.GetConfig("goose").GlobalSkillsDirectory);
        Assert.Equal(Path.Combine("/custom/xdg-config", "agents", "skills"), registry.GetConfig("amp").GlobalSkillsDirectory);
    }
}
