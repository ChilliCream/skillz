using Skillz;
using Skillz.Paths;
using Xunit;

namespace Skillz.Tests.Paths;

public sealed class SafeTreeWalkerTests : IDisposable
{
    private readonly string _root;

    public SafeTreeWalkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skillz-treewalker-" + Guid.NewGuid().ToString("N"));
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
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Creates a directory symlink, returning <see langword="false"/> when the platform
    /// refuses to create one without privilege (so the caller can skip cleanly). On the
    /// Linux CI target this always succeeds unprivileged.
    /// </summary>
    private static bool TryCreateDirectorySymlink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void Walk_Should_YieldOnlyContainedEntries_When_TreeIsPlain()
    {
        // Arrange
        var tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "a.txt"), "a");
        File.WriteAllText(Path.Combine(tree, "sub", "b.txt"), "b");

        var options = WalkOptions.ContainedTo(tree, OnSymlink.Skip);

        // Act
        var files = SafeTreeWalker.Walk(tree, options, Token)
            .Where(e => e.Kind == WalkEntryKind.File)
            .Select(e => Path.GetFileName(e.LogicalPath))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Assert
        Assert.Equal(["a.txt", "b.txt"], files);
    }

    [Fact]
    public void Walk_Should_DropOutOfRootDirSymlink_When_FollowIfContained()
    {
        // Arrange
        var tree = Path.Combine(_root, "tree");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(tree);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "x");
        if (!TryCreateDirectorySymlink(Path.Combine(tree, "escape"), outside))
        {
            return;
        }

        var options = WalkOptions.ContainedTo(tree, OnSymlink.FollowIfContained);

        // Act
        var files = SafeTreeWalker.Walk(tree, options, Token)
            .Where(e => e.Kind == WalkEntryKind.File)
            .ToList();

        // Assert
        Assert.Empty(files);
    }

    [Fact]
    public void Walk_Should_FollowInRootDirSymlink_When_FollowIfContained()
    {
        // Arrange
        var tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "real"));
        File.WriteAllText(Path.Combine(tree, "real", "inside.txt"), "x");
        if (!TryCreateDirectorySymlink(Path.Combine(tree, "alias"), Path.Combine(tree, "real")))
        {
            return;
        }

        var options = WalkOptions.ContainedTo(tree, OnSymlink.FollowIfContained);

        // Act
        var entries = SafeTreeWalker.Walk(tree, options, Token)
            .Where(e => e.Kind == WalkEntryKind.File)
            .Select(e => e.RealPath)
            .ToList();

        // Assert
        // The single real file is reached once directly under "real". The "alias"
        // symlink resolves to the same real path, which the visited-set de-dups,
        // so the file content is never yielded twice.
        Assert.Single(entries);
    }

    [Fact]
    public void Walk_Should_Throw_When_RefuseAndInTreeReparse()
    {
        // Arrange
        var tree = Path.Combine(_root, "tree");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(tree);
        Directory.CreateDirectory(outside);
        if (!TryCreateDirectorySymlink(Path.Combine(tree, "link"), outside))
        {
            return;
        }

        var options = WalkOptions.ContainedTo(tree, OnSymlink.Refuse);

        // Act & Assert
        var ex = Assert.Throws<CliException>(() => SafeTreeWalker.Walk(tree, options, Token).ToList());
        Assert.Equal(ExitCodeConstants.Failure, ex.ExitCode);
    }

    [Fact]
    public void Walk_Should_OmitReparse_When_Skip()
    {
        // Arrange
        var tree = Path.Combine(_root, "tree");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(tree);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(tree, "a.txt"), "a");
        if (!TryCreateDirectorySymlink(Path.Combine(tree, "link"), outside))
        {
            return;
        }

        var options = WalkOptions.ContainedTo(tree, OnSymlink.Skip);

        // Act
        var entries = SafeTreeWalker.Walk(tree, options, Token).ToList();
        var files = entries.Where(e => e.Kind == WalkEntryKind.File).Select(e => Path.GetFileName(e.LogicalPath)).ToList();

        // Assert
        Assert.Equal(["a.txt"], files);
        Assert.DoesNotContain(entries, e => e.LogicalPath.EndsWith("link", StringComparison.Ordinal));
    }

    [Fact]
    public void Walk_Should_Terminate_When_SelfReferentialDirSymlink()
    {
        // Arrange
        // A dir symlink pointing back at the tree root forms a cycle. The visited-set
        // (seeded with the root real path) and the depth bound must terminate it
        // rather than looping forever.
        var tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(tree);
        if (!TryCreateDirectorySymlink(Path.Combine(tree, "loop"), tree))
        {
            return;
        }

        var options = WalkOptions.ContainedTo(tree, OnSymlink.FollowIfContained, maxDepth: 64);

        // Act
        var entries = SafeTreeWalker.Walk(tree, options, Token).ToList();

        // Assert
        // It returns (does not hang); the root is yielded and the loop is de-duped
        // by the visited-set, so the entry count stays bounded.
        Assert.NotEmpty(entries);
        Assert.True(entries.Count < 64, $"Walk produced {entries.Count} entries; the cycle was not de-duplicated.");
    }

    [Fact]
    public void Walk_Should_DeduplicateRealPaths_When_TwoSymlinksShareTarget()
    {
        // Arrange
        var tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "real"));
        File.WriteAllText(Path.Combine(tree, "real", "f.txt"), "x");
        var madeA = TryCreateDirectorySymlink(Path.Combine(tree, "aliasA"), Path.Combine(tree, "real"));
        var madeB = TryCreateDirectorySymlink(Path.Combine(tree, "aliasB"), Path.Combine(tree, "real"));
        if (!madeA || !madeB)
        {
            return;
        }

        var options = WalkOptions.ContainedTo(tree, OnSymlink.FollowIfContained);

        // Act
        var fileRealPaths = SafeTreeWalker.Walk(tree, options, Token)
            .Where(e => e.Kind == WalkEntryKind.File)
            .Select(e => e.RealPath)
            .ToList();

        // Assert
        // The same real file is reachable via "real", "aliasA", and "aliasB"; the
        // visited-set must yield its real path exactly once.
        Assert.Single(fileRealPaths);
    }
}
