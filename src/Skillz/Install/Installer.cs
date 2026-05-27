using Skillz.Plugins;
using Skillz.Skills;

namespace Skillz.Install;

internal sealed class Installer : IInstaller
{
    private static readonly HashSet<string> s_excludeFiles = new(StringComparer.Ordinal) { "metadata.json" };

    private static readonly HashSet<string> s_excludeDirs = new(StringComparer.Ordinal)
    {
        ".git",
        "node_modules",
        "__pycache__",
        "__pypackages__"
    };

    private readonly IAgentRegistry _registry;
    private readonly string _home;

    public Installer(IAgentRegistry registry)
        : this(registry, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    public Installer(IAgentRegistry registry, string home)
    {
        _registry = registry;
        _home = home;
    }

    public string GetCanonicalSkillsDir(bool global, string? cwd = null)
    {
        var baseDir = global ? _home : cwd ?? Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, KnownConfigNames.AgentsDir, KnownConfigNames.SkillsSubdir);
    }

    public string GetAgentBaseDir(string agentType, bool global, string? cwd = null)
    {
        if (_registry.IsUniversalAgent(agentType))
        {
            return GetCanonicalSkillsDir(global, cwd);
        }

        var config = _registry.GetConfig(agentType);
        var baseDir = global ? _home : cwd ?? Directory.GetCurrentDirectory();

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
        var sanitized = NameSanitizer.SanitizeName(skillName);
        var canonicalBase = GetCanonicalSkillsDir(global, cwd);
        var canonicalPath = Path.Combine(canonicalBase, sanitized);

        if (!PathContainment.IsContainedIn(canonicalPath, canonicalBase))
        {
            throw new InvalidOperationException("Invalid skill name: potential path traversal detected");
        }

        return canonicalPath;
    }

    public string GetInstallPath(string skillName, string agentType, bool global = false, string? cwd = null)
    {
        var sanitized = NameSanitizer.SanitizeName(skillName);
        var targetBase = GetAgentBaseDir(agentType, global, cwd);
        var installPath = Path.Combine(targetBase, sanitized);

        if (!PathContainment.IsContainedIn(installPath, targetBase))
        {
            throw new InvalidOperationException("Invalid skill name: potential path traversal detected");
        }

        return installPath;
    }

    public async Task<InstallResult> InstallSkillForAgentAsync(
        Skill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken = default)
    {
        var config = _registry.GetConfig(agentType);
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

        var rawSkillName = string.IsNullOrEmpty(skill.Name) ? Path.GetFileName(skill.Path) : skill.Name;
        var skillName = NameSanitizer.SanitizeName(rawSkillName);

        var canonicalBase = GetCanonicalSkillsDir(isGlobal, cwd);
        var canonicalDir = Path.Combine(canonicalBase, skillName);

        var agentBase = GetAgentBaseDir(agentType, isGlobal, cwd);
        var agentDir = Path.Combine(agentBase, skillName);

        if (!PathContainment.IsContainedIn(canonicalDir, canonicalBase))
        {
            return new InstallResult(
                false,
                agentDir,
                Mode: installMode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        if (!PathContainment.IsContainedIn(agentDir, agentBase))
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
                CleanAndCreateDirectory(agentDir);
                await CopyDirectoryAsync(skill.Path, agentDir, cancellationToken).ConfigureAwait(false);

                return new InstallResult(true, agentDir, Mode: InstallMode.Copy);
            }

            CleanAndCreateDirectory(canonicalDir);
            await CopyDirectoryAsync(skill.Path, canonicalDir, cancellationToken).ConfigureAwait(false);

            if (isGlobal && _registry.IsUniversalAgent(agentType))
            {
                return new InstallResult(true, canonicalDir, canonicalDir, InstallMode.Symlink);
            }

            if (!isGlobal && !_registry.IsUniversalAgent(agentType))
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
                CleanAndCreateDirectory(agentDir);
                await CopyDirectoryAsync(skill.Path, agentDir, cancellationToken).ConfigureAwait(false);

                return new InstallResult(true, agentDir, canonicalDir, InstallMode.Symlink, SymlinkFailed: true);
            }

            return new InstallResult(true, agentDir, canonicalDir, InstallMode.Symlink);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, agentDir, Mode: installMode, Error: ex.Message);
        }
    }

    public async Task<InstallResult> InstallRemoteSkillForAgentAsync(
        RemoteSkill skill,
        string agentType,
        InstallOptions options,
        CancellationToken cancellationToken = default)
    {
        var config = _registry.GetConfig(agentType);
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

        var skillName = NameSanitizer.SanitizeName(skill.InstallName);

        var canonicalBase = GetCanonicalSkillsDir(isGlobal, cwd);
        var canonicalDir = Path.Combine(canonicalBase, skillName);

        var agentBase = GetAgentBaseDir(agentType, isGlobal, cwd);
        var agentDir = Path.Combine(agentBase, skillName);

        if (!PathContainment.IsContainedIn(canonicalDir, canonicalBase))
        {
            return new InstallResult(
                false,
                agentDir,
                Mode: installMode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        if (!PathContainment.IsContainedIn(agentDir, agentBase))
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
                CleanAndCreateDirectory(agentDir);
                if (!string.IsNullOrEmpty(skill.SourcePath) && Directory.Exists(skill.SourcePath))
                {
                    await CopyDirectoryAsync(skill.SourcePath, agentDir, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var agentSkillMd = Path.Combine(agentDir, KnownConfigNames.SkillFileName);
                    await File.WriteAllTextAsync(agentSkillMd, skill.Content, cancellationToken).ConfigureAwait(false);
                }

                return new InstallResult(true, agentDir, Mode: InstallMode.Copy);
            }

            CleanAndCreateDirectory(canonicalDir);
            if (!string.IsNullOrEmpty(skill.SourcePath) && Directory.Exists(skill.SourcePath))
            {
                await CopyDirectoryAsync(skill.SourcePath, canonicalDir, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var canonicalSkillMd = Path.Combine(canonicalDir, KnownConfigNames.SkillFileName);
                await File.WriteAllTextAsync(canonicalSkillMd, skill.Content, cancellationToken).ConfigureAwait(false);
            }

            if (isGlobal && _registry.IsUniversalAgent(agentType))
            {
                return new InstallResult(true, canonicalDir, canonicalDir, InstallMode.Symlink);
            }

            if (!isGlobal && !_registry.IsUniversalAgent(agentType))
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
                CleanAndCreateDirectory(agentDir);
                if (!string.IsNullOrEmpty(skill.SourcePath) && Directory.Exists(skill.SourcePath))
                {
                    await CopyDirectoryAsync(skill.SourcePath, agentDir, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var fallbackSkillMd = Path.Combine(agentDir, KnownConfigNames.SkillFileName);
                    await File.WriteAllTextAsync(fallbackSkillMd, skill.Content, cancellationToken)
                        .ConfigureAwait(false);
                }

                return new InstallResult(true, agentDir, canonicalDir, InstallMode.Symlink, SymlinkFailed: true);
            }

            return new InstallResult(true, agentDir, canonicalDir, InstallMode.Symlink);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, agentDir, Mode: installMode, Error: ex.Message);
        }
    }

    private static void CleanAndCreateDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
            else
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    File.Delete(path);
                }
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
        Directory.CreateDirectory(dest);

        var entries = new DirectoryInfo(src).EnumerateFileSystemInfos();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isDirectory = entry is DirectoryInfo;
            if (IsExcluded(entry.Name, isDirectory))
            {
                continue;
            }

            var srcPath = entry.FullName;
            var destPath = Path.Combine(dest, entry.Name);

            if (isDirectory)
            {
                await CopyDirectoryAsync(srcPath, destPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    var isSymlink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (isSymlink)
                    {
                        // dereference: copy the target content instead of the link itself
                        try
                        {
                            var resolved = entry.ResolveLinkTarget(returnFinalTarget: true);
                            if (resolved is FileInfo file)
                            {
                                File.Copy(file.FullName, destPath, overwrite: true);
                                continue;
                            }

                            if (resolved is DirectoryInfo dir)
                            {
                                await CopyDirectoryAsync(dir.FullName, destPath, cancellationToken)
                                    .ConfigureAwait(false);
                                continue;
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            Console.Error.WriteLine($"Skipping broken symlink: {srcPath}");
                            continue;
                        }
                        catch (DirectoryNotFoundException)
                        {
                            Console.Error.WriteLine($"Skipping broken symlink: {srcPath}");
                            continue;
                        }
                    }

                    File.Copy(srcPath, destPath, overwrite: true);
                }
                catch (FileNotFoundException) when ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Console.Error.WriteLine($"Skipping broken symlink: {srcPath}");
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
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

            var realTarget = TryGetRealPath(resolvedTarget) ?? resolvedTarget;
            var realLinkPath = TryGetRealPath(resolvedLinkPath) ?? resolvedLinkPath;

            if (PathEquals(realTarget, realLinkPath))
            {
                return true;
            }

            var realTargetWithParents = ResolveParentSymlinks(target);
            var realLinkPathWithParents = ResolveParentSymlinks(linkPath);

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
                            var resolvedExisting = ResolveSymlinkTarget(linkPath, existingTarget);
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

            var realLinkDir = ResolveParentSymlinksForDir(linkDir ?? string.Empty);
            var relativePath = Path.GetRelativePath(realLinkDir, target);

            Directory.CreateSymbolicLink(linkPath, relativePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetRealPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (resolved is not null)
                    {
                        return Path.GetFullPath(resolved.FullName);
                    }
                }

                return Path.GetFullPath(info.FullName);
            }

            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (resolved is not null)
                    {
                        return Path.GetFullPath(resolved.FullName);
                    }
                }

                return Path.GetFullPath(info.FullName);
            }
        }
        catch
        {
            // fall through
        }

        return null;
    }

    private static string ResolveParentSymlinks(string path)
    {
        var resolved = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(resolved);
        var baseName = Path.GetFileName(resolved);
        if (string.IsNullOrEmpty(dir))
        {
            return resolved;
        }

        var realDir = TryGetRealPath(dir);
        if (realDir is null)
        {
            return resolved;
        }

        return Path.Combine(realDir, baseName);
    }

    private static string ResolveParentSymlinksForDir(string dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            return dir;
        }

        var real = TryGetRealPath(dir);
        return real ?? Path.GetFullPath(dir);
    }

    private static string ResolveSymlinkTarget(string linkPath, string linkTarget)
    {
        if (Path.IsPathRooted(linkTarget))
        {
            return Path.GetFullPath(linkTarget);
        }

        var dir = Path.GetDirectoryName(linkPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(dir, linkTarget));
    }

    private static bool PathEquals(string a, string b)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(a, b, comparison);
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
