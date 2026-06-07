using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Skillz;
using Skillz.Paths;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Skillz.Tests.Net;
using Skillz.Tests.TestServices;
using Skillz.Utils;
using Xunit;

namespace Skillz.Tests.Sources.Providers;

public class WellKnownProviderTests
{
    private const string SchemaV2 = "https://schemas.agentskills.io/discovery/0.2.0/schema.json";

    private const string SampleSkillMd = "---\nname: code-review\ndescription: Review code.\n---\n# Code Review";
    private const string LegacySkillMd = "---\nname: legacy-skill\ndescription: Legacy skill.\n---\n# Legacy";

    private static string Digest(string content) => Digest(Encoding.UTF8.GetBytes(content));

    private static string Digest(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [Fact]
    public void CanHandle_Returns_True_For_WellKnown_Source()
    {
        // Arrange
        var provider = new WellKnownProvider(new FakeHttpClientFactory(new StubHttpMessageHandler()), new FakeFileStore());

        // Act & Assert
        Assert.True(provider.CanHandle(new SkillSource.WellKnown("https://example.com")));
        Assert.False(provider.CanHandle(new SkillSource.GitHub("https://github.com/foo/bar.git")));
        Assert.False(provider.CanHandle(new SkillSource.Local("/tmp/x", "/tmp/x")));
    }

    [Fact]
    public void Id_Is_WellKnown()
    {
        // Arrange
        var provider = new WellKnownProvider(new FakeHttpClientFactory(new StubHttpMessageHandler()), new FakeFileStore());

        // Act & Assert
        Assert.Equal("well-known", provider.Id);
    }

    [Fact]
    public void GetSourceIdentifier_Returns_Hostname()
    {
        // Act & Assert
        Assert.Equal("example.com", WellKnownProvider.GetSourceIdentifier("https://example.com"));
        Assert.Equal("docs.example.com", WellKnownProvider.GetSourceIdentifier("https://docs.example.com/skills"));
        Assert.Equal("mintlify.com", WellKnownProvider.GetSourceIdentifier("https://www.mintlify.com/docs"));
        Assert.Equal("unknown", WellKnownProvider.GetSourceIdentifier("not-a-url"));
    }

    [Fact]
    public async Task Fetches_Legacy_V1_Index_From_AgentSkills_Path()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            """
            {
              "skills": [
                {
                  "name": "legacy-skill",
                  "description": "Legacy skill.",
                  "files": ["SKILL.md", "references/README.md"]
                }
              ]
            }
            """);
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/legacy-skill/SKILL.md",
            LegacySkillMd,
            contentType: "text/markdown");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var skill = Assert.Single(skills);
        Assert.Equal("legacy-skill", skill.InstallName);
        Assert.Equal("legacy-skill", skill.Name);
        Assert.Equal("Legacy skill.", skill.Description);
        Assert.Equal("well-known", skill.ProviderId);
        Assert.Equal("https://example.com/.well-known/agent-skills/legacy-skill/SKILL.md", skill.SourceUrl);
    }

    [Fact]
    public async Task Falls_Back_To_Legacy_Skills_Path_When_AgentSkills_404()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRouteNotFound("https://code.claude.com/docs/.well-known/agent-skills/index.json");
        handler.AddRouteNotFound("https://code.claude.com/.well-known/agent-skills/index.json");
        handler.AddRoute(
            "https://code.claude.com/docs/.well-known/skills/index.json",
            """
            {
              "skills": [
                { "name": "claude", "description": "Claude Code.", "files": ["SKILL.md"] }
              ]
            }
            """);
        handler.AddRoute(
            "https://code.claude.com/docs/.well-known/skills/claude/SKILL.md",
            "---\nname: claude\ndescription: Claude Code.\n---\n# Claude",
            contentType: "text/markdown");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://code.claude.com/docs"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var skill = Assert.Single(skills);
        Assert.Equal("claude", skill.InstallName);
        Assert.Equal("https://code.claude.com/docs/.well-known/skills/claude/SKILL.md", skill.SourceUrl);
    }

    [Fact]
    public async Task Supports_V2_SkillMd_Entries_With_Digest_Verification()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            $$"""
            {
              "$schema": "{{SchemaV2}}",
              "skills": [
                {
                  "name": "code-review",
                  "type": "skill-md",
                  "description": "Review code.",
                  "url": "code-review/SKILL.md",
                  "digest": "{{Digest(SampleSkillMd)}}"
                }
              ]
            }
            """);
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/code-review/SKILL.md",
            SampleSkillMd,
            contentType: "text/markdown");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var skill = Assert.Single(skills);
        Assert.Equal("code-review", skill.InstallName);
        Assert.Equal("https://example.com/.well-known/agent-skills/code-review/SKILL.md", skill.SourceUrl);
        Assert.Contains("Code Review", skill.Content);
    }

    [Fact]
    public async Task Rejects_V2_SkillMd_Entries_With_Digest_Mismatch()
    {
        // Arrange
        var badDigest = $"sha256:{new string('0', 64)}";
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            $$"""
            {
              "$schema": "{{SchemaV2}}",
              "skills": [
                {
                  "name": "code-review",
                  "type": "skill-md",
                  "description": "Review code.",
                  "url": "/skills/code-review/SKILL.md",
                  "digest": "{{badDigest}}"
                }
              ]
            }
            """);
        handler.AddRoute("https://example.com/skills/code-review/SKILL.md", SampleSkillMd);

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Does_Not_Process_Unknown_Schemas()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            """
            {
              "$schema": "https://schemas.agentskills.io/discovery/9.9.9/schema.json",
              "skills": []
            }
            """);

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Returns_Empty_When_All_Endpoints_404()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRouteNotFound("https://example.com/.well-known/agent-skills/index.json");
        handler.AddRouteNotFound("https://example.com/.well-known/skills/index.json");
        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Returns_Empty_When_Index_Json_Is_Invalid()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute("https://example.com/.well-known/agent-skills/index.json", "{ not valid json");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Rejects_Legacy_Entries_With_Unsafe_File_Paths()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            """
            {
              "skills": [
                {
                  "name": "bad-skill",
                  "description": "Bad skill.",
                  "files": ["SKILL.md", "../escape.md"]
                }
              ]
            }
            """);

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Rejects_Legacy_Entries_With_Control_Byte_In_File_Path()
    {
        // Arrange: the second file carries a real ESC byte ( decodes to U+001B).
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            "{\n  \"skills\": [\n    {\n      \"name\": \"bad-skill\",\n      \"description\": \"Bad skill.\",\n      \"files\": [\"SKILL.md\", \"\\u001bevil.md\"]\n    }\n  ]\n}");
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/bad-skill/SKILL.md",
            LegacySkillMd,
            contentType: "text/markdown");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the control-byte file invalidates the whole entry, so no skill is produced even
        // though SKILL.md is reachable.
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Keeps_Valid_Legacy_Entries_When_Index_Mixes_Valid_And_Invalid()
    {
        // Arrange: one valid v1 entry plus one invalid (unsafe file path) entry.
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            """
            {
              "skills": [
                {
                  "name": "bad-skill",
                  "description": "Bad skill.",
                  "files": ["SKILL.md", "../escape.md"]
                },
                {
                  "name": "legacy-skill",
                  "description": "Legacy skill.",
                  "files": ["SKILL.md"]
                }
              ]
            }
            """);
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/legacy-skill/SKILL.md",
            LegacySkillMd,
            contentType: "text/markdown");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the valid entry survives even though an earlier entry was invalid.
        var skill = Assert.Single(skills);
        Assert.Equal("legacy-skill", skill.InstallName);
        Assert.Equal("Legacy skill.", skill.Description);
    }

    [Fact]
    public async Task Skips_V2_Entry_With_Non_Http_Artifact_Url_And_Keeps_Https_Sibling()
    {
        // Arrange: a file:// artifact URL must be skipped while a normal https entry survives.
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            $$"""
            {
              "$schema": "{{SchemaV2}}",
              "skills": [
                {
                  "name": "evil-skill",
                  "type": "skill-md",
                  "description": "Local file.",
                  "url": "file:///etc/passwd",
                  "digest": "{{Digest(SampleSkillMd)}}"
                },
                {
                  "name": "code-review",
                  "type": "skill-md",
                  "description": "Review code.",
                  "url": "code-review/SKILL.md",
                  "digest": "{{Digest(SampleSkillMd)}}"
                }
              ]
            }
            """);
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/code-review/SKILL.md",
            SampleSkillMd,
            contentType: "text/markdown");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var skill = Assert.Single(skills);
        Assert.Equal("code-review", skill.InstallName);
        // The file:// artifact must be dropped before any fetch is attempted against it.
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Scheme == "file");
    }

    [Fact]
    public async Task Rejects_Legacy_Entries_Missing_SkillMd()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            """
            {
              "skills": [
                {
                  "name": "no-skill-md",
                  "description": "No SKILL.md.",
                  "files": ["README.md"]
                }
              ]
            }
            """);

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task Rejects_V2_Entry_With_Absolute_Url_To_Different_Host_Before_Any_Fetch()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            $$"""
            {
              "$schema": "{{SchemaV2}}",
              "skills": [
                {
                  "name": "code-review",
                  "type": "skill-md",
                  "description": "Review code.",
                  "url": "https://attacker.evil.com/code-review/SKILL.md",
                  "digest": "{{Digest(SampleSkillMd)}}"
                }
              ]
            }
            """);
        handler.AddRouteNotFound("https://example.com/.well-known/skills/index.json");
        // Route the attacker target so a fetch WOULD succeed if it ever fired.
        handler.AddRoute("https://attacker.evil.com/code-review/SKILL.md", SampleSkillMd);

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
        Assert.DoesNotContain(
            handler.Requests,
            r => r.RequestUri!.Host == "attacker.evil.com");
    }

    [Fact]
    public async Task Rejects_V2_Entry_With_File_Scheme_Url_Before_Any_Fetch()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            $$"""
            {
              "$schema": "{{SchemaV2}}",
              "skills": [
                {
                  "name": "code-review",
                  "type": "skill-md",
                  "description": "Review code.",
                  "url": "file:///etc/passwd",
                  "digest": "{{Digest(SampleSkillMd)}}"
                }
              ]
            }
            """);
        handler.AddRouteNotFound("https://example.com/.well-known/skills/index.json");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
        Assert.DoesNotContain(
            handler.Requests,
            r => r.RequestUri!.Scheme == "file");
    }

    [Fact]
    public async Task Rejects_V2_Entry_With_Absolute_Url_To_Metadata_Ip_Before_Any_Fetch()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            $$"""
            {
              "$schema": "{{SchemaV2}}",
              "skills": [
                {
                  "name": "code-review",
                  "type": "skill-md",
                  "description": "Review code.",
                  "url": "https://169.254.169.254/latest/meta-data",
                  "digest": "{{Digest(SampleSkillMd)}}"
                }
              ]
            }
            """);
        handler.AddRouteNotFound("https://example.com/.well-known/skills/index.json");

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), new FakeFileStore());

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
        Assert.DoesNotContain(
            handler.Requests,
            r => r.RequestUri!.Host == "169.254.169.254");
    }

    [Fact]
    public async Task FetchSkillsAsync_Throws_On_Wrong_Source_Type()
    {
        // Arrange
        var provider = new WellKnownProvider(new FakeHttpClientFactory(new StubHttpMessageHandler()), new FakeFileStore());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.FetchSkillsAsync(
                new SkillSource.GitHub("https://github.com/foo/bar.git"),
                options: null,
                cancellationToken: TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Refuses_To_Write_Through_A_Symlinked_Legacy_Destination_Leaf()
    {
        // Arrange: the index lists a SKILL.md plus a references/README.md. The destination leaf
        // for references/README.md is turned into a symlink that escapes the staging skill dir
        // the instant its parent directory is created, so the write must refuse to follow it.
        var handler = new StubHttpMessageHandler();
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/index.json",
            """
            {
              "skills": [
                {
                  "name": "legacy-skill",
                  "description": "Legacy skill.",
                  "files": ["SKILL.md", "references/README.md"]
                }
              ]
            }
            """);
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/legacy-skill/SKILL.md",
            LegacySkillMd,
            contentType: "text/markdown");
        handler.AddRoute(
            "https://example.com/.well-known/agent-skills/legacy-skill/references/README.md",
            "PAYLOAD-THAT-MUST-NOT-BE-WRITTEN-THROUGH",
            contentType: "text/markdown");

        var planting = new SymlinkPlantingFileStore("references/README.md", "/outside/escape-target");
        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler), planting);

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the skill is still resolved (SKILL.md wrote fine), but the symlinked leaf was
        // refused - its payload bytes were never written and the escape target is untouched.
        var skill = Assert.Single(skills);
        Assert.Equal("legacy-skill", skill.InstallName);
        Assert.True(planting.RefusedLeafWrite, "the symlinked destination leaf should have been refused");
        Assert.DoesNotContain(
            planting.Inner.Files,
            pair => pair.Value.Length > 0
                && Encoding.UTF8.GetString(pair.Value).Contains("PAYLOAD-THAT-MUST-NOT-BE-WRITTEN-THROUGH", StringComparison.Ordinal));
    }
}

/// <summary>
/// Wraps a <see cref="FakeFileStore"/> and, the moment the directory whose name matches the
/// parent of <c>relativeLeak</c> is created, plants a symlink at <c>relativeLeak</c> pointing
/// at an out-of-tree target. This makes the well-known legacy write's destination leaf a
/// symlink right before the no-follow write runs, so the refusal can be asserted at the seam.
/// </summary>
file sealed class SymlinkPlantingFileStore(string relativeLeak, string escapeTarget) : IFileStore
{
    public FakeFileStore Inner { get; } = new();

    public bool RefusedLeafWrite { get; private set; }

    private readonly string _leakName = relativeLeak.Replace('\\', '/');

    public void CreateDirectory(string path)
    {
        Inner.CreateDirectory(path);

        // When the directory holding the leaked leaf is created, turn the leaf into an escaping
        // symlink so the subsequent no-follow write must refuse it.
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var parentOfLeak = _leakName.Contains('/') ? _leakName[.._leakName.LastIndexOf('/')] : string.Empty;
        if (parentOfLeak.Length > 0 && normalized.EndsWith("/" + parentOfLeak, StringComparison.Ordinal))
        {
            var leakRoot = normalized[..^(parentOfLeak.Length + 1)];
            Inner.AddSymlink(leakRoot + "/" + _leakName, escapeTarget);
        }
    }

    public Task WriteAllBytesNoFollowAsync(string path, byte[] bytes, string containRoot, CancellationToken cancellationToken)
    {
        try
        {
            return Inner.WriteAllBytesNoFollowAsync(path, bytes, containRoot, cancellationToken);
        }
        catch (CliException)
        {
            RefusedLeafWrite = true;
            throw;
        }
    }

    public bool PathExists(string path) => Inner.PathExists(path);

    public bool IsSymlink(string path) => Inner.IsSymlink(path);

    public bool FileExists(string path) => Inner.FileExists(path);

    public bool DirectoryExists(string path) => Inner.DirectoryExists(path);

    public void DeleteDirectory(string path, bool recursive) => Inner.DeleteDirectory(path, recursive);

    public void DeleteFile(string path) => Inner.DeleteFile(path);

    public void DeletePath(string path) => Inner.DeletePath(path);

    public IEnumerable<string> EnumerateDirectories(string path) => Inner.EnumerateDirectories(path);

    public bool IsDirectoryEmpty(string path) => Inner.IsDirectoryEmpty(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        => Inner.ReadAllTextAsync(path, cancellationToken);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
        => Inner.WriteAllTextAsync(path, content, cancellationToken);

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        => Inner.WriteAllBytesAsync(path, bytes, cancellationToken);

    public SafeFileHandle OpenReadNoFollow(string path, string containRoot)
        => Inner.OpenReadNoFollow(path, containRoot);

    public Task<string> ReadAllTextNoFollowAsync(string path, string containRoot, CancellationToken cancellationToken)
        => Inner.ReadAllTextNoFollowAsync(path, containRoot, cancellationToken);

    public IEnumerable<WalkEntry> Walk(string root, WalkOptions options, CancellationToken cancellationToken)
        => Inner.Walk(root, options, cancellationToken);
}
