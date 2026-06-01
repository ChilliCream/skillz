using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Skillz.Net;

internal sealed class TreeEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

internal sealed class GitHubTreeResponse
{
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("tree")]
    public List<TreeEntry>? Tree { get; set; }
}

internal sealed class RepoTree
{
    public RepoTree(string sha, string branch, ImmutableArray<TreeEntry> tree)
    {
        Sha = sha;
        Branch = branch;
        Tree = tree;
    }

    public string Sha { get; }

    public string Branch { get; }

    public ImmutableArray<TreeEntry> Tree { get; }
}
