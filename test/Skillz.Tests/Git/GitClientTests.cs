using Skillz.Git;
using Xunit;

namespace Skillz.Tests.Git;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitClientTestCollection
{
    public const string Name = "GitClient Tests";
}

[Collection(GitClientTestCollection.Name)]
public class GitClientTests
{
    [Fact]
    public async Task CloneAsync_Throws_GitCloneException_For_NonExistent_Local_Path()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        // Act
        var ex = await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal(fakeRepo, ex.Url);
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Cleans_Up_Target_Dir_On_Failure()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        // Act
        await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task CloneAsync_Respects_External_Cancellation()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CloneAsync(fakeRepo, targetDir, @ref: null, cts.Token)
        );
    }

    [Fact]
    public async Task FindDefaultBranchAsync_Returns_Null_For_NonExistent_Local_Path()
    {
        // Arrange
        var client = new GitClient();
        var fakeRepo = Path.Combine(Path.GetTempPath(), $"skillz-fake-{Guid.NewGuid():N}");

        // Act
        var result = await client.FindDefaultBranchAsync(fakeRepo, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CloneAsync_Rejects_Option_Like_Url_Target_And_Ref()
    {
        // Arrange
        var client = new GitClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CloneAsync("--upload-pack=sh", "target", @ref: null, TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CloneAsync(
                "https://example.com/repo.git",
                "--target",
                @ref: null,
                TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CloneAsync(
                "https://example.com/repo.git",
                "target",
                @ref: "-main",
                TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task FindDefaultBranchAsync_Rejects_Option_Like_Url()
    {
        // Arrange
        var client = new GitClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.FindDefaultBranchAsync("--upload-pack=sh", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task CloneAsync_Should_StripTerminalEscapesFromMessage_When_GitStderrContainsEscapes()
    {
        // Arrange
        // A stand-in 'git' on PATH fails and writes stderr that mixes terminal escape
        // sequences (a remote can have git echo these as 'remote: ...') with a URL that
        // carries credentials, so the assertions cover both stripping and redaction.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const char esc = '\x1b';
        const char bel = '\x07';
        var stderr =
            $"remote: {esc}[2J{esc}]0;title{bel}cannot fetch from "
                + $"https://user:secret@host.example/repo.git{esc}[31m\n";

        using var fakeGit = new FakeGitOnPath(exitCode: 128, stderr: stderr);
        var client = new GitClient();
        var targetDir = Path.Combine(Path.GetTempPath(), $"skillz-test-{Guid.NewGuid():N}");

        // Act
        var ex = await Assert.ThrowsAsync<GitCloneException>(() =>
            client.CloneAsync(
                "https://example.com/repo.git",
                targetDir,
                @ref: null,
                TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.DoesNotContain(esc, ex.Message);
        Assert.DoesNotContain(bel, ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
        Assert.Contains("<redacted>", ex.Message);
        Assert.Contains("cannot fetch from", ex.Message);
    }

    /// <summary>
    /// Installs a stand-in <c>git</c> executable at the front of <c>PATH</c> for the
    /// lifetime of the instance, so a clone resolves to a script with a chosen exit code
    /// and stderr. Restores the previous <c>PATH</c> on dispose.
    /// </summary>
    private sealed class FakeGitOnPath : IDisposable
    {
        private readonly string _dir;
        private readonly string? _previousPath;

        public FakeGitOnPath(int exitCode, string stderr)
        {
            _dir = Path.Combine(Path.GetTempPath(), $"skillz-fakegit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);

            var gitPath = Path.Combine(_dir, "git");
            var script = "#!/bin/sh\nprintf '%s' \"$SKILLZ_FAKE_GIT_STDERR\" 1>&2\nexit "
                + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\n";
            File.WriteAllText(gitPath, script);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    gitPath,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute);
            }

            Environment.SetEnvironmentVariable("SKILLZ_FAKE_GIT_STDERR", stderr);

            _previousPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", _dir + Path.PathSeparator + _previousPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _previousPath);
            Environment.SetEnvironmentVariable("SKILLZ_FAKE_GIT_STDERR", null);

            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
