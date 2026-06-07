using Skillz.Paths;
using Xunit;

namespace Skillz.Tests.Paths;

public class SafePathContainmentTests : IDisposable
{
    private readonly string _root;

    public SafePathContainmentTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "skillz-path-containment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // Canonicalize the temp root so expectations built with Path.GetFullPath - which tidies a
        // path string but does not follow symlinks - match SafePath's fully-resolved output. On macOS
        // the temp dir lives under /var, itself a symlink to /private/var; without this the two sides
        // would diverge only by that prefix. The symlinks each test creates under this root are still
        // resolved by the code under test, so the behavior being asserted is unaffected.
        _root = SafePath.ResolveExisting(root) ?? root;
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
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Contains_PathWithinBase_ReturnsTrue()
    {
        // Arrange
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var target = Path.Combine(baseDir, "child", "skill");

        // Act & Assert
        Assert.True(SafePath.Contains(baseDir, target, LeafPolicy.Follow));
    }

    [Fact]
    public void Contains_PathAtBase_ReturnsTrue()
    {
        // Arrange
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));

        // Act & Assert
        Assert.True(SafePath.Contains(baseDir, baseDir, LeafPolicy.Follow));
    }

    [Fact]
    public void Contains_PathOutsideBase_ReturnsFalse()
    {
        // Arrange
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var target = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "skill"));

        // Act & Assert
        Assert.False(SafePath.Contains(baseDir, target, LeafPolicy.Follow));
    }

    [Fact]
    public void Contains_PathTraversal_ReturnsFalse()
    {
        // Arrange
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var traversal = Path.Combine(baseDir, "..", "..", "etc");

        // Act & Assert
        Assert.False(SafePath.Contains(baseDir, traversal, LeafPolicy.Follow));
    }

    [Fact]
    public void Contains_SiblingDirectoryWithSamePrefix_ReturnsFalse()
    {
        // Arrange
        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base"));
        var sibling = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skillz-base-other"));

        // Act & Assert
        Assert.False(SafePath.Contains(baseDir, sibling, LeafPolicy.Follow));
    }

    [Fact]
    public void IsValidManifestRelativePath_DotSlashPrefix_ReturnsTrue()
    {
        // Act & Assert
        Assert.True(SafePath.IsValidManifestRelativePath("./skills/foo"));
    }

    [Fact]
    public void IsValidManifestRelativePath_NoPrefix_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SafePath.IsValidManifestRelativePath("skills/foo"));
    }

    [Fact]
    public void IsValidManifestRelativePath_AbsolutePath_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SafePath.IsValidManifestRelativePath("/etc/passwd"));
    }

    [Fact]
    public void IsValidManifestRelativePath_ParentTraversal_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SafePath.IsValidManifestRelativePath("../outside"));
    }

    [Fact]
    public void IsValidManifestRelativePath_EmptyString_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SafePath.IsValidManifestRelativePath(string.Empty));
    }

    [Fact]
    public void IsValidManifestRelativePath_ControlCharacter_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SafePath.IsValidManifestRelativePath("./skills/foo"));
    }

    [Fact]
    public void IsValidManifestRelativePath_NulByte_ReturnsFalse()
    {
        // A NUL would otherwise reach Path.Combine and crash discovery.
        Assert.False(SafePath.IsValidManifestRelativePath("./skills/\0foo"));
    }

    [Fact]
    public void Contains_SymlinkedBaseDirectory_ReturnsTrue()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var realBase = Path.Combine(_root, "real-base");
        var linkBase = Path.Combine(_root, "link-base");
        Directory.CreateDirectory(Path.Combine(realBase, "child"));
        Directory.CreateSymbolicLink(linkBase, realBase);

        // Act & Assert
        Assert.True(SafePath.Contains(linkBase, Path.Combine(linkBase, "child"), LeafPolicy.Preserve));
    }

    [Fact]
    public void Contains_SymlinkedParentEscapesBase_ReturnsFalse()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), outside);

        // Act & Assert
        Assert.False(SafePath.Contains(baseDir, Path.Combine(baseDir, "link", "child"), LeafPolicy.Preserve));
    }

    [Fact]
    public void Contains_ExistingTargetThroughSymlinkedParentEscapesBase_ReturnsFalse()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(outside, "child"));
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), outside);

        // Act & Assert
        Assert.False(SafePath.Contains(baseDir, Path.Combine(baseDir, "link", "child"), LeafPolicy.Preserve));
    }

    [Fact]
    public void ResolveExisting_ExistingTargetThroughSymlinkedParent_ReturnsResolvedPath()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var realBase = Path.Combine(_root, "real-base");
        var linkBase = Path.Combine(_root, "link-base");
        Directory.CreateDirectory(Path.Combine(realBase, "child"));
        Directory.CreateSymbolicLink(linkBase, realBase);

        // Act
        var result = SafePath.ResolveExisting(Path.Combine(linkBase, "child"));

        // Assert
        Assert.Equal(Path.GetFullPath(Path.Combine(realBase, "child")), result);
    }

    [Fact]
    public void Contains_DeeplyNestedNonExistentTarget_ReturnsTrue()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);

        var target = Path.Combine(baseDir, "a", "b", "c", "skill");

        // Act & Assert
        Assert.True(SafePath.Contains(baseDir, target, LeafPolicy.Preserve));
    }

    [Fact]
    public void Contains_PathTraversalAfterResolution_ReturnsFalse()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);

        var target = Path.Combine(baseDir, "..", "outside", "skill");

        // Act & Assert
        Assert.False(SafePath.Contains(baseDir, target, LeafPolicy.Preserve));
    }

    [Fact]
    public void Contains_UsesPlatformCaseSensitivity()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "CaseBase");
        Directory.CreateDirectory(baseDir);
        var differentCaseTarget = Path.Combine(_root, "casebase", "child");

        // Act
        var result = SafePath.Contains(baseDir, differentCaseTarget, LeafPolicy.Preserve);

        // Assert
        Assert.Equal(!OperatingSystem.IsLinux(), result);
    }

    [Fact]
    public void Contains_SingleNonExistentLeaf_ReturnsTrue()
    {
        // Arrange
        // A not-yet-created destination must resolve: FileInfo.Attributes returns -1
        // for a non-existent path, and that sentinel is guarded so a normal install
        // target is not treated as unresolvable.
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);

        var target = Path.Combine(baseDir, "skill-not-created-yet");

        // Act & Assert
        Assert.True(SafePath.Contains(baseDir, target, LeafPolicy.Preserve));
    }

    [Fact]
    public void Contains_LeafSymlinkPointingOutside_ReturnsTrue()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The leaf is a symlink escaping the base, but it lives directly inside
        // the base. Because we only resolve the parent chain (not the leaf) under
        // the Preserve policy, it is reported as contained - the caller replaces
        // the link safely. This is what makes idempotent reinstall of a
        // skillz-managed agent->canonical symlink work.
        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "leaf"), outside);

        // Act & Assert
        Assert.True(SafePath.Contains(baseDir, Path.Combine(baseDir, "leaf"), LeafPolicy.Preserve));
    }

    [Fact]
    public void ResolveForCreate_NonExistentTarget_ReturnsParentResolvedPath()
    {
        // Arrange
        // Regression for the FileInfo.Attributes == -1 bug: a non-existent
        // nested target must resolve to its nearest existing parent (with
        // symlinks followed) plus the missing segments - never null.
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

        // Act
        var resolved = SafePath.ResolveForCreate(target);

        // Assert
        Assert.Equal(Path.GetFullPath(Path.Combine(realBase, "a", "b", "skill")), resolved);
    }
}
