using System.Text.Json.Serialization;
using Skillz.Commands;
using Skillz.Lock;
using Skillz.Net;
using Skillz.Plugins;
using Skillz.Sources.Providers;

namespace Skillz;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(MarketplaceManifest))]
[JsonSerializable(typeof(SinglePluginManifest))]
[JsonSerializable(typeof(SkillLockFile))]
[JsonSerializable(typeof(SkillLockEntry))]
[JsonSerializable(typeof(LocalSkillLockFile))]
[JsonSerializable(typeof(LocalSkillLockEntry))]
[JsonSerializable(typeof(GitHubTreeResponse))]
[JsonSerializable(typeof(SearchApiResponse))]
[JsonSerializable(typeof(WellKnownIndex))]
[JsonSerializable(typeof(InstalledSkillJson[]))]
internal partial class JsonSourceGenerationContext : JsonSerializerContext;
