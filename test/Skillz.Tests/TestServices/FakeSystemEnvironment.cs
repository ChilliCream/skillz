using Skillz.Install;

namespace Skillz.Tests.TestServices;

internal sealed class FakeSystemEnvironment : ISystemEnvironment
{
    public string HomeDirectory { get; set; } = "/home/test";
    public string CurrentDirectory { get; set; } = "/workspace";
    public Dictionary<string, string?> Env { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Dirs { get; init; } = new(StringComparer.Ordinal);
    public string? GetEnvironmentVariable(string name) => Env.GetValueOrDefault(name);
    public bool DirectoryExists(string path) => Dirs.Contains(path);
}
