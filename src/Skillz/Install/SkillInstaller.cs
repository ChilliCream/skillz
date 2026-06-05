using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Utils;

namespace Skillz.Install;

internal sealed class SkillInstaller(AgentRegistry registry, ISystemEnvironment system, IFileStore fileStore)
    : ISkillInstaller
{
    private static readonly HashSet<string> s_excludeFiles = new(StringComparer.Ordinal) { "metadata.json" };

    private static readonly HashSet<string> s_excludeDirs = new(StringComparer.Ordinal)
    {
        ".git",
        "node_modules",
        "__pycache__",
        "__pypackages__"
    };

    public string GetCanonicalSkillsDirectory(bool global, string? workingDirectory = null)
    {
        var baseDirectory = global ? system.HomeDirectory : workingDirectory ?? system.CurrentDirectory;
        return Path.Combine(baseDirectory, KnownConfigNames.AgentsDirectory, KnownConfigNames.SkillsSubdirectory);
    }

    public string GetAgentBaseDirectory(string agentType, bool global, string? workingDirectory = null)
    {
        if (registry.IsUniversalAgent(agentType))
        {
            return GetCanonicalSkillsDirectory(global, workingDirectory);
        }

        var config = registry.GetConfig(agentType);
        var baseDirectory = global ? system.HomeDirectory : workingDirectory ?? system.CurrentDirectory;

        if (global)
        {
            if (config.GlobalSkillsDirectory is null)
            {
                return Path.Combine(baseDirectory, NormalizeRelative(config.SkillsDirectory));
            }

            return config.GlobalSkillsDirectory;
        }

        return Path.Combine(baseDirectory, NormalizeRelative(config.SkillsDirectory));
    }

    // Registry skills directories are authored with forward slashes (e.g. ".claude/skills").
    // Path.Combine leaves those interior separators untouched, so on Windows the result would be
    // ".claude/skills" instead of the native ".claude\skills". Rewrite them to the platform
    // separator so callers (and equality checks against Path.Combine-built paths) see a native path.
    private static string NormalizeRelative(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);

    public string GetCanonicalPath(string skillName, bool global = false, string? workingDirectory = null)
    {
        var sanitized = SkillNameSanitizer.SanitizeName(skillName);
        var canonicalBase = GetCanonicalSkillsDirectory(global, workingDirectory);
        var canonicalPath = Path.Combine(canonicalBase, sanitized);

        if (!PathContainment.IsContainedInRealPath(canonicalPath, canonicalBase))
        {
            throw new InvalidOperationException("Invalid skill name: potential path traversal detected");
        }

        return canonicalPath;
    }

    public string GetInstallPath(string skillName, string agentType, bool global = false, string? workingDirectory = null)
    {
        var sanitized = SkillNameSanitizer.SanitizeName(skillName);
        var targetBase = GetAgentBaseDirectory(agentType, global, workingDirectory);
        var installPath = Path.Combine(targetBase, sanitized);

        if (!PathContainment.IsContainedInRealPath(installPath, targetBase))
        {
            throw new InvalidOperationException("Invalid skill name: potential path traversal detected");
        }

        return installPath;
    }

    public async Task<InstallResult> InstallAsync(
        ResolvedSkill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken)
    {
        var config = registry.GetConfig(agentType);
        var isGlobal = options.Global;
        var workingDirectory = options.WorkingDirectory ?? system.CurrentDirectory;
        var installMode = options.Mode;

        if (isGlobal && config.GlobalSkillsDirectory is null)
        {
            return new InstallResult(
                Success: false,
                Path: string.Empty,
                Mode: installMode,
                Error: $"{config.DisplayName} does not support global skill installation");
        }

        var skillName = SkillNameSanitizer.SanitizeName(skill.InstallName);

        var canonicalBase = GetCanonicalSkillsDirectory(isGlobal, workingDirectory);
        var canonicalDirectory = Path.Combine(canonicalBase, skillName);

        var agentBase = GetAgentBaseDirectory(agentType, isGlobal, workingDirectory);
        var agentDirectory = Path.Combine(agentBase, skillName);

        if (!PathContainment.IsContainedInRealPath(canonicalDirectory, canonicalBase))
        {
            return new InstallResult(
                false,
                agentDirectory,
                Mode: installMode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        if (!PathContainment.IsContainedInRealPath(agentDirectory, agentBase))
        {
            return new InstallResult(
                false,
                agentDirectory,
                Mode: installMode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        try
        {
            if (installMode == InstallMode.Copy)
            {
                CleanAndCreateDirectory(agentDirectory, agentBase);
                await MaterializeAsync(skill, agentDirectory, cancellationToken);

                return new InstallResult(true, agentDirectory, Mode: InstallMode.Copy);
            }

            CleanAndCreateDirectory(canonicalDirectory, canonicalBase);
            await MaterializeAsync(skill, canonicalDirectory, cancellationToken);

            if (isGlobal && registry.IsUniversalAgent(agentType))
            {
                return new InstallResult(true, canonicalDirectory, canonicalDirectory, InstallMode.Symlink);
            }

            if (!isGlobal && !registry.IsUniversalAgent(agentType))
            {
                var rootSegment = config.SkillsDirectory.Split('/', '\\')[0];
                var agentRootDirectory = Path.Combine(workingDirectory, rootSegment);
                if (!Directory.Exists(agentRootDirectory))
                {
                    return new InstallResult(true, canonicalDirectory, canonicalDirectory, InstallMode.Symlink, Skipped: true);
                }
            }

            ClearSkillDestination(agentDirectory, canonicalDirectory);

            var symlinkCreated = TryCreateSymlink(canonicalDirectory, agentDirectory);

            if (!symlinkCreated)
            {
                CleanAndCreateDirectory(agentDirectory, agentBase);
                await MaterializeAsync(skill, agentDirectory, cancellationToken);

                return new InstallResult(true, agentDirectory, canonicalDirectory, InstallMode.Symlink, SymlinkFailed: true);
            }

            return new InstallResult(true, agentDirectory, canonicalDirectory, InstallMode.Symlink);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InstallResult(false, agentDirectory, Mode: installMode, Error: ex.Message);
        }
    }

    private async Task MaterializeAsync(ResolvedSkill skill, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(skill.SourcePath) && Directory.Exists(skill.SourcePath))
        {
            await CopyDirectoryAsync(skill.SourcePath, destinationDirectory, cancellationToken);
        }
        else
        {
            var skillMd = Path.Combine(destinationDirectory, KnownConfigNames.SkillFileName);
            await File.WriteAllTextAsync(skillMd, skill.Content, cancellationToken);
        }
    }

    private void CleanAndCreateDirectory(string path, string containmentBase)
    {
        if (!PathContainment.IsContainedInRealPath(path, containmentBase))
        {
            throw new InvalidOperationException("Install destination escapes its expected root");
        }

        try
        {
            // Delete whatever is there first. A reparse point (symlink / self-loop) is
            // removed as a link, never recursing through it - so replacing a symlinked
            // destination can never touch whatever it points at.
            fileStore.DeletePath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed clean must fail the install: CreateDirectory below is a silent
            // no-op on an existing directory, so swallowing this would leave stale files
            // from a prior skill in place and the materialize step would merge new files
            // over them, producing a corrupt skill while reporting success.
            throw new CliException(
                1,
                $"Failed to clean install destination '{path}': {ex.Message}",
                title: "Install destination could not be cleaned",
                hint: "Remove or fix permissions on the destination directory and try again.");
        }

        fileStore.CreateDirectory(path);

        // The destination must be empty before we materialize into it. CreateDirectory is
        // a no-op when the path already exists, so a successful-looking clean that left
        // contents behind would otherwise silently merge into the new skill.
        if (!fileStore.IsDirectoryEmpty(path))
        {
            throw new CliException(
                1,
                $"Install destination '{path}' is not empty after cleaning",
                title: "Install destination could not be cleaned",
                hint: "Remove the destination directory and try again.");
        }
    }

    private static async Task CopyDirectoryAsync(string src, string dest, CancellationToken cancellationToken)
    {
        var sourceRoot =
            RealPath.TryGetRealPath(src)
            ?? throw new InvalidOperationException($"Source path cannot be resolved: {src}");

        await CopyDirectoryAsync(src, dest, sourceRoot, depth: 0, cancellationToken);
    }

    private static async Task CopyDirectoryAsync(
        string src,
        string dest,
        string sourceRoot,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 50)
        {
            throw new InvalidOperationException($"Maximum directory recursion depth exceeded at: {src}. A cyclic symlink may be present.");
        }

        Directory.CreateDirectory(dest);

        var entries = new DirectoryInfo(src).EnumerateFileSystemInfos();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isReparsePoint = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            var isDirectoryEntry = entry is DirectoryInfo;
            var isDirectory = isDirectoryEntry && !isReparsePoint;
            if (IsExcluded(entry.Name, isDirectoryEntry))
            {
                continue;
            }

            var srcPath = entry.FullName;
            var destPath = Path.Combine(dest, entry.Name);

            if (isReparsePoint)
            {
                await CopyReparsePointTargetAsync(entry, destPath, sourceRoot, depth + 1, cancellationToken);
            }
            else if (isDirectory)
            {
                await CopyDirectoryAsync(srcPath, destPath, sourceRoot, depth + 1, cancellationToken);
            }
            else
            {
                File.Copy(srcPath, destPath, overwrite: true);
            }
        }

        await Task.CompletedTask;
    }

    private static async Task CopyReparsePointTargetAsync(
        FileSystemInfo entry,
        string destPath,
        string sourceRoot,
        int depth,
        CancellationToken cancellationToken)
    {
        FileSystemInfo? resolved;
        try
        {
            resolved = entry.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidOperationException($"Refusing to copy broken symlink: {entry.FullName}", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new InvalidOperationException($"Refusing to copy broken symlink: {entry.FullName}", ex);
        }

        // A broken link does not always throw above: ResolveLinkTarget can
        // return a non-null FileInfo whose Exists is false. Treat any target
        // that does not actually exist as a broken symlink.
        if (resolved is not { Exists: true })
        {
            throw new InvalidOperationException($"Refusing to copy broken symlink: {entry.FullName}");
        }

        if (!PathContainment.IsContainedInRealPath(resolved.FullName, sourceRoot))
        {
            throw new InvalidOperationException($"Refusing to copy symlink outside source root: {entry.FullName}");
        }

        switch (resolved)
        {
            case FileInfo file:
                File.Copy(file.FullName, destPath, overwrite: true);
                break;
            case DirectoryInfo dir:
                await CopyDirectoryAsync(dir.FullName, destPath, sourceRoot, depth + 1, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Refusing to copy unsupported symlink target: {entry.FullName}");
        }
    }

    private static bool IsExcluded(string name, bool isDirectory)
    {
        if (s_excludeFiles.Contains(name))
        {
            return true;
        }

        if (isDirectory && s_excludeDirs.Contains(name))
        {
            return true;
        }

        return false;
    }

    private static bool TryCreateSymlink(string target, string linkPath)
    {
        try
        {
            // Lexically normalize both paths (collapse "." / ".." segments).
            var resolvedTarget = Path.GetFullPath(target);
            var resolvedLinkPath = Path.GetFullPath(linkPath);

            // Fully resolve symlinks on both. If they already point at the same place, the
            // link we want effectively exists - nothing to do.
            var realTarget = RealPath.TryGetRealPath(resolvedTarget) ?? resolvedTarget;
            var realLinkPath = RealPath.TryGetRealPath(resolvedLinkPath) ?? resolvedLinkPath;

            if (PathEquals(realTarget, realLinkPath))
            {
                return true;
            }

            // Same check, resolving only the nearest existing parents - handles the case where
            // the leaf (link or target) does not exist on disk yet.
            var realTargetWithParents = RealPath.ResolveWithNearestExistingParent(target) ?? resolvedTarget;
            var realLinkPathWithParents = RealPath.ResolveWithNearestExistingParent(linkPath) ?? resolvedLinkPath;

            if (PathEquals(realTargetWithParents, realLinkPathWithParents))
            {
                return true;
            }

            // Ensure the link's parent directory exists.
            var linkDir = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(linkDir))
            {
                Directory.CreateDirectory(linkDir);
            }

            // Resolve both endpoints the same way, then compute the relative target between them.
            // Mixing a resolved dir with a raw target yields a link that breaks when a parent is
            // a symlink.
            var realLinkDir =
                RealPath.ResolveWithNearestExistingParent(linkDir ?? string.Empty)
                ?? Path.GetFullPath(linkDir ?? string.Empty);
            var realTargetForLink =
                RealPath.ResolveWithNearestExistingParent(target) ?? Path.GetFullPath(target);
            var relativePath = Path.GetRelativePath(realLinkDir, realTargetForLink);

            // Create the relative symlink. This is the only filesystem mutation here:
            // TryCreateSymlink never deletes a symlink, directory, or file. If something
            // already occupies linkPath, CreateSymbolicLink throws IOException and we return
            // false below - clearing a prior install is the caller's responsibility
            // (see ClearSkillDestination).
            Directory.CreateSymbolicLink(linkPath, relativePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Any failure - including an already-occupied destination - yields false with no
            // deletion performed.
            return false;
        }
    }

    /// <summary>
    /// Clears a prior install at <paramref name="path"/> so a fresh symlink can be placed
    /// there. Ownership-guarded (refuses to delete a real directory that is not a
    /// skillz-managed skill) and self-deletion-safe (refuses to delete when the path already
    /// resolves to the canonical store it would otherwise serve).
    /// </summary>
    private void ClearSkillDestination(string path, string canonicalDirectory)
    {
        // Nothing there at all - no file, no directory, no (possibly broken) reparse point.
        if (!fileStore.PathExists(path))
        {
            return;
        }

        // The destination already resolves to the canonical store (a universal agent whose skills
        // dir IS the store, or an agent skills dir that is itself a symlink into it). It already
        // holds the content - leave it untouched; the symlink step is then a no-op.
        var realPath = RealPath.ResolveWithNearestExistingParent(path);
        var realCanonical = RealPath.ResolveWithNearestExistingParent(canonicalDirectory);
        if (realPath is not null && realCanonical is not null && PathEquals(realPath, realCanonical))
        {
            return;
        }

        try
        {
            // A symlink: unlink it. DeletePath removes a reparse point as a link without recursing
            // into its target, so no data is lost.
            if (fileStore.IsSymlink(path))
            {
                fileStore.DeletePath(path);
                return;
            }

            // A real directory: only remove it when empty. We never delete directory contents.
            if (fileStore.DirectoryExists(path))
            {
                if (!fileStore.IsDirectoryEmpty(path))
                {
                    throw new CliException(
                        ExitCodeConstants.Failure,
                        $"Refusing to overwrite '{path}': a non-empty directory already exists there.",
                        title: "Install destination is not empty",
                        hint: "Remove the directory yourself, then run the command again.");
                }

                fileStore.DeleteDirectory(path, recursive: false);
                return;
            }

            // A real file: never delete it.
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Refusing to overwrite the existing file at '{path}'.",
                title: "Install destination already exists",
                hint: "Remove the file yourself, then run the command again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Failed to clean install destination '{path}': {ex.Message}",
                title: "Install destination could not be cleaned",
                hint: "Remove or fix permissions on the destination and try again.");
        }
    }

    private static bool PathEquals(string a, string b)
    {
        return string.Equals(a, b, PathContainment.Comparison);
    }
}
