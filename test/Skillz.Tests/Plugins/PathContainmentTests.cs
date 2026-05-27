using Skillz.Plugins;
using Xunit;

namespace Skillz.Tests.Plugins;

public class PathContainmentTests
{
    [Fact]
    public void IsContainedIn_PathWithinBase_ReturnsTrue()
    {
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var target = Path.Combine(baseDir, "child", "skill");

        Assert.True(PathContainment.IsContainedIn(target, baseDir));
    }

    [Fact]
    public void IsContainedIn_PathAtBase_ReturnsTrue()
    {
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));

        Assert.True(PathContainment.IsContainedIn(baseDir, baseDir));
    }

    [Fact]
    public void IsContainedIn_PathOutsideBase_ReturnsFalse()
    {
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var target = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "skill"));

        Assert.False(PathContainment.IsContainedIn(target, baseDir));
    }

    [Fact]
    public void IsContainedIn_PathTraversal_ReturnsFalse()
    {
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var traversal = Path.Combine(baseDir, "..", "..", "etc");

        Assert.False(PathContainment.IsContainedIn(traversal, baseDir));
    }

    [Fact]
    public void IsContainedIn_SiblingDirectoryWithSamePrefix_ReturnsFalse()
    {
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var sibling = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base-other"));

        Assert.False(PathContainment.IsContainedIn(sibling, baseDir));
    }

    [Fact]
    public void IsValidRelativePath_DotSlashPrefix_ReturnsTrue()
    {
        Assert.True(PathContainment.IsValidRelativePath("./skills/foo"));
    }

    [Fact]
    public void IsValidRelativePath_NoPrefix_ReturnsFalse()
    {
        Assert.False(PathContainment.IsValidRelativePath("skills/foo"));
    }

    [Fact]
    public void IsValidRelativePath_AbsolutePath_ReturnsFalse()
    {
        Assert.False(PathContainment.IsValidRelativePath("/etc/passwd"));
    }

    [Fact]
    public void IsValidRelativePath_ParentTraversal_ReturnsFalse()
    {
        Assert.False(PathContainment.IsValidRelativePath("../outside"));
    }

    [Fact]
    public void IsValidRelativePath_EmptyString_ReturnsFalse()
    {
        Assert.False(PathContainment.IsValidRelativePath(string.Empty));
    }
}
