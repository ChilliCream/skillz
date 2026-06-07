namespace Skillz.Paths;

/// <summary>
/// Options for <see cref="SafeTreeWalker.Walk"/>. One walk, parameterized by intent,
/// serving copy + hash + discovery. <see cref="MaxDepth"/> defaults to a safe
/// 64 via the <see cref="ContainedTo"/> factory; the raw struct also requires it so a
/// caller cannot silently get an unbounded (or zero) walk.
/// </summary>
internal readonly record struct WalkOptions
{
    /// <summary>
    /// The real-path root every yielded entry must stay inside. Required; there is no uncontained walk.
    /// </summary>
    public required string ContainRoot { get; init; }

    /// <summary>
    /// Hard recursion bound (cyclic-symlink and pathological-depth guard). Required - no silent default.
    /// </summary>
    public required int MaxDepth { get; init; }

    /// <summary>
    /// Behaviour on a reparse point. Required - caller states intent.
    /// </summary>
    public required OnSymlink OnSymlink { get; init; }

    /// <summary>
    /// Directory/file names to skip entirely (e.g. ".git", "node_modules"). Compared with <see cref="SafePath.Comparer"/>.
    /// </summary>
    public IReadOnlySet<string>? SkipNames { get; init; }

    /// <summary>
    /// Builds <see cref="WalkOptions"/> contained to <paramref name="root"/>, with a safe
    /// default depth bound so a caller cannot silently get an unbounded walk.
    /// </summary>
    /// <param name="root">The real-path root every yielded entry must stay inside.</param>
    /// <param name="onSymlink">Behaviour on a reparse point.</param>
    /// <param name="maxDepth">Hard recursion bound; defaults to a safe 64.</param>
    /// <param name="skip">Directory/file names to skip entirely.</param>
    public static WalkOptions ContainedTo(string root, OnSymlink onSymlink, int maxDepth = 64, IReadOnlySet<string>? skip = null)
        => new() { ContainRoot = root, OnSymlink = onSymlink, MaxDepth = maxDepth, SkipNames = skip };
}
