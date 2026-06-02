using System.Security.Cryptography;
using System.Text;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Skillz.Tests.Net;
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
        var provider = new WellKnownProvider(new FakeHttpClientFactory(new StubHttpMessageHandler()));

        // Act & Assert
        Assert.True(provider.CanHandle(new SkillSource.WellKnown("https://example.com")));
        Assert.False(provider.CanHandle(new SkillSource.GitHub("https://github.com/foo/bar.git")));
        Assert.False(provider.CanHandle(new SkillSource.Local("/tmp/x", "/tmp/x")));
    }

    [Fact]
    public void Id_Is_WellKnown()
    {
        // Arrange
        var provider = new WellKnownProvider(new FakeHttpClientFactory(new StubHttpMessageHandler()));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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
        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
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

        var provider = new WellKnownProvider(new FakeHttpClientFactory(handler));

        // Act
        var skills = await provider.FetchSkillsAsync(
            new SkillSource.WellKnown("https://example.com"),
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task FetchSkillsAsync_Throws_On_Wrong_Source_Type()
    {
        // Arrange
        var provider = new WellKnownProvider(new FakeHttpClientFactory(new StubHttpMessageHandler()));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.FetchSkillsAsync(
                new SkillSource.GitHub("https://github.com/foo/bar.git"),
                options: null,
                cancellationToken: TestContext.Current.CancellationToken)
        );
    }
}
