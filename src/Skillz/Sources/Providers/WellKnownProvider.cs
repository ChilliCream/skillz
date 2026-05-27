using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Skillz.Plugins;
using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal sealed partial class WellKnownProvider : IProvider
{
    internal const string HttpClientName = "Skillz.WellKnown";
    internal const string DiscoverySchemaV2 = "https://schemas.agentskills.io/discovery/0.2.0/schema.json";

    private static readonly string[] s_wellKnownPaths = [".well-known/agent-skills", ".well-known/skills"];
    private const string IndexFile = "index.json";

    [GeneratedRegex(@"^sha256:[a-f0-9]{64}$")]
    private static partial Regex DigestRegex();

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex SkillNameRegex();

    private readonly IHttpClientFactory _httpClientFactory;

    public WellKnownProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string Id => "well-known";

    public bool CanHandle(ParsedSource source) => source is ParsedSource.WellKnown;

    public async Task<IReadOnlyList<RemoteSkill>> FetchSkillsAsync(
        ParsedSource source,
        ProviderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is not ParsedSource.WellKnown wellKnown)
        {
            throw new ArgumentException(
                $"WellKnownProvider cannot handle {source.GetType().Name}.",
                nameof(source));
        }

        var candidates = await FetchIndexCandidatesAsync(wellKnown.Url, cancellationToken)
            .ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var skills = new List<RemoteSkill>();
            foreach (var entry in candidate.Entries)
            {
                var skill = await FetchSkillByEntryAsync(entry, cancellationToken).ConfigureAwait(false);
                if (skill is not null)
                {
                    skills.Add(skill);
                }
            }

            if (skills.Count > 0)
            {
                return skills;
            }
        }

        return [];
    }

    internal static string GetSourceIdentifier(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return "unknown";
        }

        var host = parsed.Host;
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private async Task<List<IndexCandidate>> FetchIndexCandidatesAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            return [];
        }

        var basePath = parsed.AbsolutePath.TrimEnd('/');
        var origin = $"{parsed.Scheme}://{parsed.Authority}";

        var urlsToTry = new List<(string IndexUrl, string ResolvedBaseUrl, string WellKnownPath)>();
        foreach (var wellKnownPath in s_wellKnownPaths)
        {
            urlsToTry.Add((
                $"{origin}{basePath}/{wellKnownPath}/{IndexFile}",
                origin + basePath,
                wellKnownPath));

            if (!string.IsNullOrEmpty(basePath))
            {
                urlsToTry.Add((
                    $"{origin}/{wellKnownPath}/{IndexFile}",
                    origin,
                    wellKnownPath));
            }
        }

        var candidates = new List<IndexCandidate>();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        foreach (var (indexUrl, resolvedBase, wellKnownPath) in urlsToTry)
        {
            try
            {
                using var response = await client.GetAsync(indexUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var normalized = NormalizeIndex(json, indexUrl, resolvedBase, wellKnownPath);
                if (normalized is not null)
                {
                    candidates.Add(normalized);
                }
            }
            catch (HttpRequestException)
            {
                continue;
            }
            catch (TaskCanceledException)
            {
                continue;
            }
        }

        return candidates;
    }

    private static IndexCandidate? NormalizeIndex(
        string json,
        string indexUrl,
        string resolvedBaseUrl,
        string wellKnownPath)
    {
        WellKnownIndex? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, JsonSourceGenerationContext.Default.WellKnownIndex);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null || parsed.Skills is null)
        {
            return null;
        }

        if (parsed.Schema == DiscoverySchemaV2)
        {
            var entries = new List<NormalizedEntry>();
            foreach (var entry in parsed.Skills)
            {
                if (!IsValidSkillEntryV2(entry))
                {
                    continue;
                }

                if (!Uri.TryCreate(new Uri(indexUrl, UriKind.Absolute), entry.Url, out var artifactUri))
                {
                    continue;
                }

                entries.Add(new NormalizedEntry(
                    Version: "0.2.0",
                    Name: entry.Name!,
                    Description: entry.Description!,
                    Type: entry.Type,
                    ArtifactUrl: artifactUri.ToString(),
                    Digest: entry.Digest,
                    Files: null,
                    BaseUrl: null,
                    WellKnownPath: null));
            }

            if (entries.Count == 0)
            {
                return null;
            }

            return new IndexCandidate(entries, resolvedBaseUrl, wellKnownPath);
        }

        if (parsed.Schema is not null)
        {
            return null;
        }

        var legacyEntries = new List<NormalizedEntry>();
        var legacyBaseUrl = GetLegacySkillBaseUrl(indexUrl, wellKnownPath);
        foreach (var entry in parsed.Skills)
        {
            if (!IsValidSkillEntryV1(entry))
            {
                return null;
            }

            legacyEntries.Add(new NormalizedEntry(
                Version: "0.1.0",
                Name: entry.Name!,
                Description: entry.Description!,
                Type: null,
                ArtifactUrl: null,
                Digest: null,
                Files: entry.Files,
                BaseUrl: legacyBaseUrl,
                WellKnownPath: wellKnownPath));
        }

        return new IndexCandidate(legacyEntries, resolvedBaseUrl, wellKnownPath);
    }

    private static string GetLegacySkillBaseUrl(string indexUrl, string wellKnownPath)
    {
        var parsed = new Uri(indexUrl);
        var marker = $"/{wellKnownPath}/{IndexFile}";
        var pathname = parsed.AbsolutePath;
        var trimmed = pathname.EndsWith(marker, StringComparison.Ordinal)
            ? pathname[..^marker.Length]
            : pathname;
        return $"{parsed.Scheme}://{parsed.Authority}{trimmed}";
    }

    private async Task<RemoteSkill?> FetchSkillByEntryAsync(
        NormalizedEntry entry,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        if (entry.Version == "0.1.0")
        {
            return await FetchLegacySkillAsync(client, entry, cancellationToken).ConfigureAwait(false);
        }

        if (entry.Type == "skill-md")
        {
            return await FetchSkillMdArtifactAsync(client, entry, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<RemoteSkill?> FetchLegacySkillAsync(
        HttpClient client,
        NormalizedEntry entry,
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "skillz-" + Guid.NewGuid().ToString("N"));
        var skillDir = Path.Combine(stagingRoot, entry.Name);

        try
        {
            var skillBaseUrl = $"{entry.BaseUrl!.TrimEnd('/')}/{entry.WellKnownPath}/{entry.Name}";
            var skillMdUrl = $"{skillBaseUrl}/SKILL.md";

            using var response = await client.GetAsync(skillMdUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                TempDirCleanup.SafeDelete(stagingRoot);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var fm = FrontmatterParser.Parse(content);
            if (!fm.Data.TryGetValue("name", out var nameObj) || nameObj is not string skillName
                || !fm.Data.TryGetValue("description", out var descObj) || descObj is not string skillDesc)
            {
                TempDirCleanup.SafeDelete(stagingRoot);
                return null;
            }

            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(
                Path.Combine(skillDir, "SKILL.md"),
                content,
                cancellationToken).ConfigureAwait(false);

            if (entry.Files is not null)
            {
                foreach (var relativeFile in entry.Files)
                {
                    if (string.Equals(relativeFile, "SKILL.md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!IsSafeLegacyFilePath(relativeFile))
                    {
                        continue;
                    }

                    var fileUrl = $"{skillBaseUrl}/{relativeFile}";
                    try
                    {
                        using var fileResponse = await client.GetAsync(fileUrl, cancellationToken).ConfigureAwait(false);
                        if (!fileResponse.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        var bytes = await fileResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                        var normalizedRelative = relativeFile.Replace('\\', '/');
                        var destination = Path.Combine(skillDir, normalizedRelative.Replace('/', Path.DirectorySeparatorChar));

                        if (!PathContainment.IsContainedIn(destination, skillDir))
                        {
                            continue;
                        }

                        var parent = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
                    }
                    catch (HttpRequestException)
                    {
                    }
                    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }
            }

            return new RemoteSkill(
                Name: TerminalSanitizer.SanitizeMetadata(skillName),
                Description: TerminalSanitizer.SanitizeMetadata(skillDesc),
                Content: content,
                InstallName: entry.Name,
                SourceUrl: skillMdUrl,
                ProviderId: Id,
                SourceIdentifier: GetSourceIdentifier(entry.BaseUrl!),
                SourcePath: skillDir,
                CleanupPath: stagingRoot);
        }
        catch (HttpRequestException)
        {
            TempDirCleanup.SafeDelete(stagingRoot);
            return null;
        }
        catch (TaskCanceledException)
        {
            TempDirCleanup.SafeDelete(stagingRoot);
            return null;
        }
        catch
        {
            TempDirCleanup.SafeDelete(stagingRoot);
            throw;
        }
    }

    private async Task<RemoteSkill?> FetchSkillMdArtifactAsync(
        HttpClient client,
        NormalizedEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(entry.ArtifactUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (ComputeDigest(bytes) != entry.Digest)
            {
                return null;
            }

            var content = Encoding.UTF8.GetString(bytes);
            var fm = FrontmatterParser.Parse(content);
            if (!fm.Data.TryGetValue("name", out var nameObj) || nameObj is not string skillName
                || !fm.Data.TryGetValue("description", out var descObj) || descObj is not string skillDesc)
            {
                return null;
            }

            return new RemoteSkill(
                Name: TerminalSanitizer.SanitizeMetadata(skillName),
                Description: TerminalSanitizer.SanitizeMetadata(skillDesc),
                Content: content,
                InstallName: entry.Name,
                SourceUrl: entry.ArtifactUrl!,
                ProviderId: Id,
                SourceIdentifier: GetSourceIdentifier(entry.ArtifactUrl!));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static string ComputeDigest(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
#if NET9_0_OR_GREATER
        return $"sha256:{Convert.ToHexStringLower(hash)}";
#else
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
#endif
    }

    private static bool IsValidSkillName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64)
        {
            return false;
        }

        if (!SkillNameRegex().IsMatch(name))
        {
            return false;
        }

        if (name.StartsWith('-') || name.EndsWith('-') || name.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool IsSafeLegacyFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        if (filePath.StartsWith('/') || filePath.StartsWith('\\') || filePath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (filePath.Contains('\0'))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidSkillEntryV1(WellKnownIndexEntry entry)
    {
        if (!IsValidSkillName(entry.Name))
        {
            return false;
        }

        if (string.IsNullOrEmpty(entry.Description))
        {
            return false;
        }

        if (entry.Files is null || entry.Files.Count == 0)
        {
            return false;
        }

        foreach (var file in entry.Files)
        {
            if (!IsSafeLegacyFilePath(file))
            {
                return false;
            }
        }

        var hasSkillMd = entry.Files.Any(f => string.Equals(f, "SKILL.md", StringComparison.OrdinalIgnoreCase));
        return hasSkillMd;
    }

    private static bool IsValidSkillEntryV2(WellKnownIndexEntry entry)
    {
        if (!IsValidSkillName(entry.Name))
        {
            return false;
        }

        if (string.IsNullOrEmpty(entry.Description) || entry.Description.Length > 1024)
        {
            return false;
        }

        if (entry.Type is not "skill-md" and not "archive")
        {
            return false;
        }

        if (string.IsNullOrEmpty(entry.Url))
        {
            return false;
        }

        if (entry.Digest is null || !DigestRegex().IsMatch(entry.Digest))
        {
            return false;
        }

        return true;
    }

    private sealed record IndexCandidate(
        IReadOnlyList<NormalizedEntry> Entries,
        string ResolvedBaseUrl,
        string WellKnownPath);

    private sealed record NormalizedEntry(
        string Version,
        string Name,
        string Description,
        string? Type,
        string? ArtifactUrl,
        string? Digest,
        IReadOnlyList<string>? Files,
        string? BaseUrl,
        string? WellKnownPath);
}
