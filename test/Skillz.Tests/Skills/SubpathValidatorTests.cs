using Skillz.Skills;
using Xunit;

namespace Skillz.Tests.Skills;

public class SubpathValidatorTests : IDisposable
{
    private readonly string _root;

    public SubpathValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skillz-subpath-validator-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("skills/my-skill")]
    [InlineData("path/to/skill")]
    [InlineData("src")]
    public void ValidateSubpath_Allows_Normal_Subpaths(string input)
    {
        // Act & Assert
        Assert.Equal(input, SubpathValidator.ValidateSubpath(input));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("../../etc/passwd")]
    [InlineData("skills/../../etc")]
    [InlineData("a/b/../../../etc")]
    public void ValidateSubpath_Rejects_DotDot_Segments(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => SubpathValidator.ValidateSubpath(input));

        // Assert
        Assert.Contains("Unsafe subpath", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..\\etc")]
    [InlineData("..\\..\\secret")]
    public void ValidateSubpath_Rejects_Backslash_Traversal(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => SubpathValidator.ValidateSubpath(input));

        // Assert
        Assert.Contains("Unsafe subpath", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("file.txt")]
    [InlineData("path/to/.config")]
    [InlineData("..skill")]
    [InlineData("skill..")]
    public void ValidateSubpath_Allows_Dots_That_Are_Not_Traversal(string input)
    {
        // Act & Assert
        Assert.Equal(input, SubpathValidator.ValidateSubpath(input));
    }

    [Theory]
    [InlineData("/tmp/repo", "skills")]
    [InlineData("/tmp/repo", "skills/my-skill")]
    [InlineData("/tmp/repo", "a/b/c")]
    public void IsSubpathSafe_Returns_True_For_Subpaths_Within_BasePath(string basePath, string subpath)
    {
        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe(basePath, subpath));
    }

    [Theory]
    [InlineData("/tmp/repo", "..")]
    [InlineData("/tmp/repo", "../etc")]
    [InlineData("/tmp/repo", "../../etc/passwd")]
    [InlineData("/tmp/repo", "skills/../../..")]
    public void IsSubpathSafe_Returns_False_For_Subpaths_Escaping_BasePath(string basePath, string subpath)
    {
        // Act & Assert
        Assert.False(SubpathValidator.IsSubpathSafe(basePath, subpath));
    }

    [Fact]
    public void IsSubpathSafe_Rejects_Any_DotDot_Segment_Even_When_It_Stays_Within()
    {
        // A ".." segment is rejected up front: it can never be trusted before
        // symlink resolution (Path.GetFullPath would collapse it lexically and
        // hide an escaping symlinked parent), and the source spec already
        // rejects ".." earlier via ValidateSubpath.
        Assert.False(SubpathValidator.IsSubpathSafe("/tmp/repo", "skills/../other"));
        Assert.False(SubpathValidator.IsSubpathSafe("/tmp/repo", "skills/.."));
    }

    [Fact]
    public void IsSubpathSafe_Allows_Single_Dot_Resolving_To_BasePath()
    {
        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe("/tmp/repo", "."));
    }

    [Fact]
    public void IsSubpathSafe_Returns_False_When_Subpath_Is_Symlink_Escaping_BasePath()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "repo");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);
        // /repo/link -> /outside (outside the base) — discovery must not follow it.
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), outside);

        // Act & Assert
        Assert.False(SubpathValidator.IsSubpathSafe(baseDir, "link"));
    }

    [Fact]
    public void IsSubpathSafe_Returns_False_When_Subpath_Passes_Through_Symlinked_Parent_Escaping_BasePath()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "repo");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(outside, "skill"));
        // /repo/link -> /outside; the target lives under the escaping symlinked parent.
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), outside);

        // Act & Assert
        Assert.False(SubpathValidator.IsSubpathSafe(baseDir, "link/skill"));
    }

    [Fact]
    public void IsSubpathSafe_Returns_True_When_Subpath_Is_Legitimate_Nested_RealSubdir()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(baseDir, "skills", "my-skill"));

        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe(baseDir, "skills/my-skill"));
    }

    [Fact]
    public void IsSubpathSafe_Returns_False_When_Subpath_Contains_DotDot_Traversal()
    {
        // Arrange
        var baseDir = Path.Combine(_root, "repo");
        Directory.CreateDirectory(baseDir);

        // Act & Assert
        Assert.False(SubpathValidator.IsSubpathSafe(baseDir, "../outside"));
    }

    [Fact]
    public void IsSubpathSafe_Returns_True_When_Subpath_Is_Symlink_Staying_Within_BasePath()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(baseDir, "real-skill"));
        // /repo/link -> /repo/real-skill (still inside the base) is allowed.
        Directory.CreateSymbolicLink(Path.Combine(baseDir, "link"), Path.Combine(baseDir, "real-skill"));

        // Act & Assert
        Assert.True(SubpathValidator.IsSubpathSafe(baseDir, "link"));
    }
}
