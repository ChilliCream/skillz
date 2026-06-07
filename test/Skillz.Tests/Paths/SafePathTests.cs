using Skillz;
using Skillz.Paths;
using Xunit;

namespace Skillz.Tests.Paths;

public sealed class SafePathTests : IDisposable
{
    private readonly string _root;

    public SafePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skillz-safepath-" + Guid.NewGuid().ToString("N"));
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

    private static bool TryCreateFileSymlink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
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
    public void Contains_Should_ReturnTrue_When_TargetIsContained()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(Path.Combine(baseDir, "child"));
        var target = Path.Combine(baseDir, "child", "skill");

        // Act
        var unified = SafePath.Contains(baseDir, target, LeafPolicy.Preserve);

        // Assert
        Assert.True(unified);
    }

    [Fact]
    public void Contains_Should_ReturnFalse_When_SymlinkedParentEscapes()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        if (!TryCreateDirectorySymlink(Path.Combine(baseDir, "link"), outside))
        {
            return;
        }

        var target = Path.Combine(baseDir, "link", "child");

        // Act
        var unified = SafePath.Contains(baseDir, target, LeafPolicy.Preserve);

        // Assert
        Assert.False(unified);
    }

    [Fact]
    public void Contains_Should_TreatManagedLeafSymlinkAsContained_When_PreservePolicy()
    {
        // Arrange
        // The leaf is a symlink escaping the base but living directly inside it.
        // Preserve must NOT treat it as an escape (it is the skillz-managed
        // agent->canonical link the caller replaces safely).
        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        if (!TryCreateDirectorySymlink(Path.Combine(baseDir, "leaf"), outside))
        {
            return;
        }

        var target = Path.Combine(baseDir, "leaf");

        // Act
        var unified = SafePath.Contains(baseDir, target, LeafPolicy.Preserve);

        // Assert
        Assert.True(unified);
    }

    [Fact]
    public void Contains_Should_ReturnFalse_When_DotDotCollapsesPastSymlink()
    {
        // Arrange
        // root/link -> outside. The target root/link/../x lexically collapses to
        // root/x, but must be rejected BEFORE resolution because the '..' would
        // otherwise hop out through the symlink.
        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        if (!TryCreateDirectorySymlink(Path.Combine(baseDir, "link"), outside))
        {
            return;
        }

        var target = Path.Combine(baseDir, "link", "..", "x");

        // Act
        var result = SafePath.Contains(baseDir, target, LeafPolicy.Follow);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_Should_ReturnFalse_When_SymlinkedParentEscapes_FollowFollow()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(outside, "child"));
        if (!TryCreateDirectorySymlink(Path.Combine(baseDir, "link"), outside))
        {
            return;
        }

        var target = Path.Combine(baseDir, "link", "child");

        // Act
        var result = SafePath.Contains(baseDir, target, LeafPolicy.Follow);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("skills/foo", true)]
    [InlineData("a.txt", true)]
    [InlineData("/etc/passwd", false)]
    [InlineData("dir\\sub", false)]
    [InlineData("../outside", false)]
    [InlineData("a/../b", false)]
    [InlineData("skills/foo", false)]
    [InlineData("", false)]
    public void IsValidStoredRelative_Should_MatchTruthTable(string relative, bool expected)
    {
        // Act
        var result = SafePath.IsValidStoredRelative(relative);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(!expected, SafePath.IsUnsafeStoredRelative(relative));
    }

    [Fact]
    public void IsValidStoredRelative_Should_ReturnFalse_When_Null()
    {
        // Act & Assert
        Assert.False(SafePath.IsValidStoredRelative(null));
        Assert.True(SafePath.IsUnsafeStoredRelative(null));
    }

    [Fact]
    public async Task WriteAllBytesNoFollowAsync_Should_Refuse_When_LeafIsSymlinkOutsideRoot()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        var outsideFile = Path.Combine(_root, "secret.txt");
        Directory.CreateDirectory(baseDir);
        await File.WriteAllTextAsync(outsideFile, "secret", Token);
        var leaf = Path.Combine(baseDir, "leaf");
        if (!TryCreateFileSymlink(leaf, outsideFile))
        {
            return;
        }

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CliException>(
            () => SafePath.WriteAllBytesNoFollowAsync(leaf, [1, 2, 3], baseDir, Token));
        Assert.Equal(ExitCodeConstants.Failure, ex.ExitCode);
        // The symlink target must be untouched - the write did not follow through.
        Assert.Equal("secret", await File.ReadAllTextAsync(outsideFile, Token));
    }

    [Fact]
    public async Task WriteAllBytesNoFollowAsync_Should_Refuse_When_TrailingSeparatorOnSymlinkLeaf()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        var outsideDir = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outsideDir);
        var leaf = Path.Combine(baseDir, "link");
        if (!TryCreateDirectorySymlink(leaf, outsideDir))
        {
            return;
        }

        // The "dir/link/" trailing-separator form must still be refused.
        var withTrailingSeparator = leaf + Path.DirectorySeparatorChar;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CliException>(
            () => SafePath.WriteAllBytesNoFollowAsync(withTrailingSeparator, [1], baseDir, Token));
        Assert.Equal(ExitCodeConstants.Failure, ex.ExitCode);
    }

    [Fact]
    public async Task WriteAllBytesNoFollowAsync_Should_WriteFile_When_LeafIsRegular()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);
        var target = Path.Combine(baseDir, "out.bin");
        var payload = new byte[] { 9, 8, 7 };

        // Act
        await SafePath.WriteAllBytesNoFollowAsync(target, payload, baseDir, Token);

        // Assert
        Assert.Equal(payload, await File.ReadAllBytesAsync(target, Token));
    }

    [Fact]
    public async Task ReadAllTextNoFollowAsync_Should_Refuse_When_LeafIsSymlink()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        var outsideFile = Path.Combine(_root, "secret.txt");
        Directory.CreateDirectory(baseDir);
        await File.WriteAllTextAsync(outsideFile, "secret-bytes", Token);
        var leaf = Path.Combine(baseDir, "skill.md");
        if (!TryCreateFileSymlink(leaf, outsideFile))
        {
            return;
        }

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CliException>(
            () => SafePath.ReadAllTextNoFollowAsync(leaf, baseDir, Token));
        Assert.Equal(ExitCodeConstants.Failure, ex.ExitCode);
    }

    [Fact]
    public void OpenReadNoFollow_Should_Refuse_When_LeafIsSymlink()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        var outsideFile = Path.Combine(_root, "secret.txt");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(outsideFile, "secret-bytes");
        var leaf = Path.Combine(baseDir, "leaf");
        if (!TryCreateFileSymlink(leaf, outsideFile))
        {
            return;
        }

        // Act & Assert
        var ex = Assert.Throws<CliException>(() => SafePath.OpenReadNoFollow(leaf, baseDir));
        Assert.Equal(ExitCodeConstants.Failure, ex.ExitCode);
    }

    [Fact]
    public async Task ReadAllTextNoFollowAsync_Should_ReturnContent_When_LeafIsRegular()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "base");
        Directory.CreateDirectory(baseDir);
        var file = Path.Combine(baseDir, "regular.txt");
        await File.WriteAllTextAsync(file, "hello", Token);

        // Act
        var content = await SafePath.ReadAllTextNoFollowAsync(file, baseDir, Token);

        // Assert
        Assert.Equal("hello", content);
    }
}
