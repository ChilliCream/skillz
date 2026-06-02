using System.Collections.Immutable;

namespace Skillz.Net;

internal sealed class RepoTree(string sha, string branch, ImmutableArray<TreeEntry> tree)
{
    public string Sha { get; } = sha;

    public string Branch { get; } = branch;

    public ImmutableArray<TreeEntry> Tree { get; } = tree;
}
