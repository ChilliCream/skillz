namespace Skillz.Install;

internal sealed class SystemEnvironment : ISystemEnvironment
{
    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string CurrentDirectory => Directory.GetCurrentDirectory();
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}
