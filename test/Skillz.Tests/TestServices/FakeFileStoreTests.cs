using Xunit;

namespace Skillz.Tests.TestServices;

public class FakeFileStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void EnumerateDirectories_Should_Return_Only_Immediate_Children_When_Tree_Is_Nested()
    {
        // Arrange
        var store = new FakeFileStore();
        store.CreateDirectory("/root/a");
        store.CreateDirectory("/root/b");
        store.CreateDirectory("/root/a/deep");

        // Act
        var children = store.EnumerateDirectories("/root").OrderBy(d => d, StringComparer.Ordinal).ToArray();

        // Assert
        Assert.Equal(new[] { "/root/a", "/root/b" }, children);
    }

    [Fact]
    public void EnumerateDirectories_Should_Throw_DirectoryNotFound_When_Directory_Missing()
    {
        // Arrange
        var store = new FakeFileStore();

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => store.EnumerateDirectories("/missing").ToArray());
    }

    [Fact]
    public void CreateDirectory_Should_Register_Ancestors_When_Path_Is_Nested()
    {
        // Arrange
        var store = new FakeFileStore();

        // Act
        store.CreateDirectory("/root/a/b");

        // Assert
        Assert.True(store.DirectoryExists("/root"));
        Assert.True(store.DirectoryExists("/root/a"));
        Assert.True(store.DirectoryExists("/root/a/b"));
    }

    [Fact]
    public async Task WriteAllTextAsync_Should_Register_Containing_Directory_When_File_Written()
    {
        // Arrange
        var store = new FakeFileStore();

        // Act
        await store.WriteAllTextAsync("/root/skills/SKILL.md", "content", Token);

        // Assert
        Assert.True(store.FileExists("/root/skills/SKILL.md"));
        Assert.True(store.DirectoryExists("/root/skills"));
        Assert.Equal("content", await store.ReadAllTextAsync("/root/skills/SKILL.md", Token));
    }

    [Fact]
    public void IsDirectoryEmpty_Should_Return_True_When_Missing_Or_Empty()
    {
        // Arrange
        var store = new FakeFileStore();
        store.CreateDirectory("/root/empty");

        // Act & Assert
        Assert.True(store.IsDirectoryEmpty("/missing"));
        Assert.True(store.IsDirectoryEmpty("/root/empty"));
    }

    [Fact]
    public async Task IsDirectoryEmpty_Should_Return_False_When_Contains_File_Or_Subdirectory()
    {
        // Arrange
        var store = new FakeFileStore();
        store.CreateDirectory("/with-file");
        await store.WriteAllTextAsync("/with-file/a.txt", "x", Token);
        store.CreateDirectory("/with-dir/child");

        // Act & Assert
        Assert.False(store.IsDirectoryEmpty("/with-file"));
        Assert.False(store.IsDirectoryEmpty("/with-dir"));
    }

    [Fact]
    public async Task DeleteDirectory_Should_Remove_Subtree_And_Files_When_Recursive()
    {
        // Arrange
        var store = new FakeFileStore();
        store.CreateDirectory("/root/keep");
        store.CreateDirectory("/root/drop/nested");
        await store.WriteAllTextAsync("/root/drop/file.txt", "x", Token);

        // Act
        store.DeleteDirectory("/root/drop", recursive: true);

        // Assert
        Assert.False(store.DirectoryExists("/root/drop"));
        Assert.False(store.DirectoryExists("/root/drop/nested"));
        Assert.False(store.FileExists("/root/drop/file.txt"));
        Assert.True(store.DirectoryExists("/root/keep"));
    }
}
