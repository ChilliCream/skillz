using Skillz.Plugins;
using Skillz.Skills;

namespace Skillz.Install;

internal sealed class SkillInstaller(AgentRegistry registry, string home) : ISkillInstaller
{
    private static readonly HashSet<string> s_excludeFiles = new(StringComparer.Ordinal) { "metadata.json" };

    private static readonly HashSet<string> s_excludeDirs = new(StringComparer.Ordinal)
    {
        ".git",
        "node_modules",
        "__pycache__",
        "__pypackages__"
    };

    public SkillInstaller(AgentRegistry registry)
        : this(registry, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    public string GetCanonicalSkillsDir(bool global, string? cwd = null)
    {
        var baseDir = global ? home : cwd ?? Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, KnownConfigNames.AgentsDir, KnownConfigNames.SkillsSubdir);
    }

    public string GetAgentBaseDir(string agentType, bool global, string? cwd = null)
    {
        if (registry.IsUniversalAgent(agentType))
        {
            return GetCanonicalSkillsDir(global, cwd);
        }

        var config = registry.GetConfig(agentType);
        var baseDir = global ? home : cwd ?? Directory.GetCurrentDirectory();

        if (global)
        {
            if (config.GlobalSkillsDir is null)
            {
                return Path.Combine(baseDir, config.SkillsDir);
            }

            return config.GlobalSkillsDir;
        }

        return Path.Combine(baseDir, config.SkillsDir);
    }

    public string GetCanonicalPath(string skillName, bool global = false, string? cwd = null)
    {
        var sanitized = SkillNameSanitizer.SanitizeName(skillName);
        var canonicalBase = GetCanonicalSkillsDir(global, cwd);
        var canonicalPath = Path.Combine(canonicalBase, sanitized);

        if (!PathContainment.IsContainedInRealPath(canonicalPath, canonicalBase))
        {
            throw new InvalidOperationException("Invalid skill name: potential path traversal detected");
        }

        return canonicalPath;
    }

    public string GetInstallPath(string skillName, string agentType, bool global = false, string? cwd = null)
    {
        var sanitized = SkillNameSanitizer.SanitizeName(skillName);
        var targetBase = GetAgentBaseDir(agentType, global, cwd);
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
        var cwd = options.Cwd ?? Directory.GetCurrentDirectory();
        var installMode = options.Mode;

        if (isGlobal && config.GlobalSkillsDir is null)
        {
            return new InstallResult(
                Success: false,
                Path: string.Empty,
                Mode: installMode,
                Error: $"{config.DisplayName} does not support global skill installation");
        }

        var skillName = SkillNameSanitizer.SanitizeName(skill.InstallName);

        var canonicalBase = GetCanonicalSkillsDir(isGlobal, cwd);
        var canonicalDir = Path.Combine(canonicalBase, skillName);

        var agentBase = GetAgentBaseDir(agentType, isGlobal, cwd);
        var agentDir = Path.Combine(agentBase, skillName);

        if (!PathContainment.IsContainedInRealPath(canonicalDir, canonicalBase))
        {
            return new InstallResult(
                false,
                agentDir,
                Mode: installMode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        if (!PathContainment.IsContainedInRealPath(agentDir, agentBase))
        {
            return new InstallResult(
                false,
                agentDir,
                Mode: installMode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        try
        {
            if (installMode == InstallMode.Copy)
            {
                CleanAndCreateDirectory(agentDir, agentBase);
                await MaterializeAsync(skill, agentDir, cancellationToken);

                return new InstallResult(true, agentDir, Mode: InstallMode.Copy);
            }

            CleanAndCreateDirectory(canonicalDir, canonicalBase);
            await MaterializeAsync(skill, canonicalDir, cancellationToken);

            if (isGlobal && registry.IsUniversalAgent(agentType))
            {
                return new InstallResult(true, canonicalDir, canonicalDir, InstallMode.Symlink);
            }

            if (!isGlobal && !registry.IsUniversalAgent(agentType))
            {
                var rootSegment = config.SkillsDir.Split('/', '\\')[0];
                var agentRootDir = Path.Combine(cwd, rootSegment);
                if (!Directory.Exists(agentRootDir))
                {
                    return new InstallResult(true, canonicalDir, canonicalDir, InstallMode.Symlink, Skipped: true);
                }
            }

            var symlinkCreated = TryCreateSymlink(canonicalDir, agentDir);

            if (!symlinkCreated)
            {
                CleanAndCreateDirectory(agentDir, agentBase);
                await MaterializeAsync(skill, agentDir, cancellationToken);

                return new InstallResult(true, agentDir, canonicalDir, InstallMode.Symlink, SymlinkFailed: true);
            }

            return new InstallResult(true, agentDir, canonicalDir, InstallMode.Symlink);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, agentDir, Mode: installMode, Error: ex.Message);
        }
    }

    private async Task MaterializeAsync(ResolvedSkill skill, string destDir, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(skill.SourcePath) && Directory.Exists(skill.SourcePath))
        {
            await CopyDirectoryAsync(skill.SourcePath, destDir, ct);
        }
        else
        {
            var skillMd = Path.Combine(destDir, KnownConfigNames.SkillFileName);
            await File.WriteAllTextAsync(skillMd, skill.Content, ct);
        }
    }

    private static void CleanAndCreateDirectory(string path, string containmentBase)
    {
        if (!PathContainment.IsContainedInRealPath(path, containmentBase))
        {
            throw new InvalidOperationException("Install destination escapes its expected root");
        }

        try
        {
            // Delete any reparse point (symlink / self-loop) as a link first,
            // never recursing through it — so replacing a symlinked
            // destination can never touch whatever it points at.
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteReparsePoint(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // mkdir below will surface a real problem
        }

        Directory.CreateDirectory(path);
    }

    private static async Task CopyDirectoryAsync(string src, string dest, CancellationToken cancellationToken)
    {
        var sourceRoot =
            RealPath.TryGetRealPath(src)
            ?? throw new InvalidOperationException($"Source path cannot be resolved: {src}");

        await CopyDirectoryAsync(src, dest, sourceRoot, cancellationToken);
    }

    private static async Task CopyDirectoryAsync(
        string src,
        string dest,
        string sourceRoot,
        CancellationToken cancellationToken)
    {
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
                await CopyReparsePointTargetAsync(entry, destPath, sourceRoot, cancellationToken);
            }
            else if (isDirectory)
            {
                await CopyDirectoryAsync(srcPath, destPath, sourceRoot, cancellationToken);
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
                await CopyDirectoryAsync(dir.FullName, destPath, sourceRoot, cancellationToken);
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
            var resolvedTarget = Path.GetFullPath(target);
            var resolvedLinkPath = Path.GetFullPath(linkPath);

            var realTarget = RealPath.TryGetRealPath(resolvedTarget) ?? resolvedTarget;
            var realLinkPath = RealPath.TryGetRealPath(resolvedLinkPath) ?? resolvedLinkPath;

            if (PathEquals(realTarget, realLinkPath))
            {
                return true;
            }

            var realTargetWithParents = RealPath.ResolveWithNearestExistingParent(target) ?? resolvedTarget;
            var realLinkPathWithParents = RealPath.ResolveWithNearestExistingParent(linkPath) ?? resolvedLinkPath;

            if (PathEquals(realTargetWithParents, realLinkPathWithParents))
            {
                return true;
            }

            try
            {
                var info = new FileInfo(linkPath);
                if (info.Exists
                    || Directory.Exists(linkPath)
                    || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        var existingTarget = info.LinkTarget;
                        if (existingTarget is not null)
                        {
                            var resolvedExisting = RealPath.ResolveSymlinkTarget(linkPath, existingTarget);
                            if (PathEquals(resolvedExisting, resolvedTarget))
                            {
                                return true;
                            }
                        }

                        DeleteReparsePoint(linkPath);
                    }
                    else if (Directory.Exists(linkPath))
                    {
                        Directory.Delete(linkPath, recursive: true);
                    }
                    else
                    {
                        File.Delete(linkPath);
                    }
                }
            }
            catch
            {
                try
                {
                    DeleteReparsePoint(linkPath);
                }
                catch
                {
                    // If we can't remove it, symlink creation will fail below
                }
            }

            var linkDir = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(linkDir))
            {
                Directory.CreateDirectory(linkDir);
            }

            var realLinkDir =
                RealPath.ResolveWithNearestExistingParent(linkDir ?? string.Empty)
                ?? Path.GetFullPath(linkDir ?? string.Empty);
            var relativePath = Path.GetRelativePath(realLinkDir, target);

            Directory.CreateSymbolicLink(linkPath, relativePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathEquals(string a, string b)
    {
        return string.Equals(a, b, PathContainment.Comparison);
    }

    private static void DeleteReparsePoint(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(path);
        }
        catch (IOException)
        {
            Directory.Delete(path);
        }
    }
}
