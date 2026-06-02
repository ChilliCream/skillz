namespace Skillz.Install;

/// <summary>
/// Abstracts the ambient operating-system state the install layer depends on —
/// environment variables, directory existence, and well-known directories — so that
/// it can be substituted in tests instead of touching the real process and filesystem.
/// </summary>
internal interface ISystemEnvironment
{
    /// <summary>
    /// The current user's home directory (for example, <c>/home/alice</c>), used as the
    /// base for agent configuration paths.
    /// </summary>
    string HomeDirectory { get; }

    /// <summary>
    /// The current working directory, used to detect project-local agent configuration.
    /// </summary>
    string CurrentDirectory { get; }

    /// <summary>
    /// Reads an environment variable, returning its raw value or <see langword="null"/>
    /// when it is not set. Callers are responsible for any trimming or normalization.
    /// </summary>
    /// <param name="name">The name of the environment variable to read.</param>
    /// <returns>The variable's value, or <see langword="null"/> if it is undefined.</returns>
    string? GetEnvironmentVariable(string name);

    /// <summary>
    /// Determines whether a directory exists at the given path.
    /// </summary>
    /// <param name="path">The directory path to probe.</param>
    /// <returns><see langword="true"/> if the directory exists; otherwise <see langword="false"/>.</returns>
    bool DirectoryExists(string path);
}
