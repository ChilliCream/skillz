using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Skillz.Paths;

/// <summary>
/// Computes a content hash of a skill tree without following symlinks out of tree.
/// The walk skips reparse points entirely so out-of-tree bytes never enter the
/// digest, bounds recursion so a directory-symlink cycle terminates, and re-checks
/// every leaf with a no-follow open.
/// </summary>
internal static class SafeTreeHash
{
    private static readonly ImmutableHashSet<string> s_skip = new HashSet<string>(StringComparer.Ordinal)
    {
        ".git",
        "node_modules"
    }.ToImmutableHashSet();

    /// <summary>
    /// Computes a lowercase hex SHA-256 digest over the in-tree files of
    /// <paramref name="skillDirectory"/>. Files are ordered by their relative path so the
    /// digest is deterministic, each relative path is mixed in before its content, and every
    /// leaf is opened with a no-follow open so a symlinked leaf is refused rather than read
    /// through.
    /// </summary>
    /// <param name="skillDirectory">The root of the skill tree to hash.</param>
    /// <param name="cancellationToken">A token to cancel the hash.</param>
    public static string ComputeTreeHash(string skillDirectory, CancellationToken cancellationToken)
    {
        // Skip reparse points entirely: out-of-tree bytes never enter the digest.
        var options = WalkOptions.ContainedTo(skillDirectory, OnSymlink.Skip, maxDepth: 64, skip: s_skip);

        var files = new List<(string Rel, string Real)>();
        foreach (var entry in SafeTreeWalker.Walk(skillDirectory, options, cancellationToken))
        {
            if (entry.Kind == WalkEntryKind.File)
            {
                files.Add((Path.GetRelativePath(skillDirectory, entry.LogicalPath).Replace('\\', '/'), entry.RealPath));
            }
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.Rel, b.Rel));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8192];

        foreach (var (rel, real) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(rel));

            // No-follow open, defense in depth: the walk already skipped reparse
            // entries, but the open re-checks the leaf so the digest can never include
            // bytes from behind a leaf symlink.
            using var handle = SafePath.OpenReadNoFollow(real, skillDirectory);
            long offset = 0;
            int read;
            while ((read = RandomAccess.Read(handle, buffer, offset)) > 0)
            {
                hash.AppendData(buffer[..read]);
                offset += read;
            }
        }

        var bytes = hash.GetHashAndReset();
#if NET9_0_OR_GREATER
        return Convert.ToHexStringLower(bytes);
#else
        return Convert.ToHexString(bytes).ToLowerInvariant();
#endif
    }
}
