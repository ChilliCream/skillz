using Skillz.Plugins;
using Xunit;

namespace Skillz.Tests.Plugins;

public class PathContainmentTests : IDisposable
{
    private readonly string _root;

    public PathContainmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skillz-path-containment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

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

    [Fact]
    public void IsContainedInRealPath_SymlinkedBaseDirectory_ReturnsTrue()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var realBase = Path.Combine(_root, "real-base");
        var linkBase = Path.Combine(_root, "link-base");
        Directory.CreateDirectory(Path.Combine(realBase, "child"));
        Directory.CreateSymbolicLink(linkBase, realBase);

        Assert.True(PathContainment.IsContainedInRealPath(Path.Combine(linkBase, "child"), linkBase));
    }

    [Fact]
    public void IsContainedInRealPath_SymlinkedParentEscapesBase_ReturnsFalse()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), outside);

        Assert.False(PathContainment.IsContainedInRealPath(Path.Combine(baseDir, "link", "child"), baseDir));
    }

    [Fact]
    public void IsContainedInRealPath_ExistingTargetThroughSymlinkedParentEscapesBase_ReturnsFalse()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(outside, "child"));
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), outside);

        Assert.False(PathContainment.IsContainedInRealPath(Path.Combine(baseDir, "link", "child"), baseDir));
    }

    [Fact]
    public void TryGetRealPath_ExistingTargetThroughSymlinkedParent_ReturnsResolvedPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var realBase = Path.Combine(_root, "real-base");
        var linkBase = Path.Combine(_root, "link-base");
        Directory.CreateDirectory(Path.Combine(realBase, "child"));
        Directory.CreateSymbolicLink(linkBase, realBase);

        var result = RealPath.TryGetRealPath(Path.Combine(linkBase, "child"));

        Assert.Equal(Path.GetFullPath(Path.Combine(realBase, "child")), result);
    }

    [Fact]
    public void IsContainedInRealPath_DeeplyNestedNonExistentTarget_ReturnsTrue()
    {
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);

        var target = Path.Combine(baseDir, "a", "b", "c", "skill");

        Assert.True(PathContainment.IsContainedInRealPath(target, baseDir));
    }

    [Fact]
    public void IsContainedInRealPath_PathTraversalAfterResolution_ReturnsFalse()
    {
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);

        var target = Path.Combine(baseDir, "..", "outside", "skill");

        Assert.False(PathContainment.IsContainedInRealPath(target, baseDir));
    }

    [Fact]
    public void IsContainedInRealPath_UsesPlatformCaseSensitivity()
    {
        var baseDir = Path.Combine(_root, "CaseBase");
        Directory.CreateDirectory(baseDir);
        var differentCaseTarget = Path.Combine(_root, "casebase", "child");

        var result = PathContainment.IsContainedInRealPath(differentCaseTarget, baseDir);

        Assert.Equal(!OperatingSystem.IsLinux(), result);
    }

    [Fact]
    public void IsContainedInRealPath_SingleNonExistentLeaf_ReturnsTrue()
    {
        // Regression: FileInfo.Attributes returns -1 for a non-existent path,
        // which previously made the resolver treat every not-yet-created
        // destination as unresolvable and reject the (normal) install.
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);

        var target = Path.Combine(baseDir, "skill-not-created-yet");

        Assert.True(PathContainment.IsContainedInRealPath(target, baseDir));
    }

    [Fact]
    public void IsContainedInRealPath_LeafSymlinkPointingOutside_ReturnsTrue()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The leaf is a symlink escaping the base, but it lives directly inside
        // the base. Because we only resolve the parent chain (not the leaf), it
        // is reported as contained — the caller replaces the link safely. This
        // is what makes idempotent reinstall of a skillz-managed agent->canonical
        // symlink work.
        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "leaf"), outside);

        Assert.True(PathContainment.IsContainedInRealPath(Path.Combine(baseDir, "leaf"), baseDir));
    }

    [Fact]
    public void ResolveWithNearestExistingParent_NonExistentTarget_ReturnsParentResolvedPath()
    {
        // Regression for the FileInfo.Attributes == -1 bug: a non-existent
        // nested target must resolve to its nearest existing parent (with
        // symlinks followed) plus the missing segments — never null.
        var realBase = Path.Combine(_root, "real-base");
        Directory.CreateDirectory(realBase);

        string baseForLookup;
        if (OperatingSystem.IsWindows())
        {
            baseForLookup = realBase;
        }
        else
        {
            // Put a symlinked parent in the way to prove the parent chain is
            // followed while the missing leaf segments are appended verbatim.
            var linkBase = Path.Combine(_root, "link-base");
            Directory.CreateSymbolicLink(linkBase, realBase);
            baseForLookup = linkBase;
        }

        var target = Path.Combine(baseForLookup, "a", "b", "skill");

        var resolved = RealPath.ResolveWithNearestExistingParent(target);

        Assert.Equal(Path.GetFullPath(Path.Combine(realBase, "a", "b", "skill")), resolved);
    }
}
