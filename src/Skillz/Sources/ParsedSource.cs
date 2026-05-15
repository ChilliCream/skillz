namespace Skillz.Sources;

internal abstract record ParsedSource
{
    private ParsedSource()
    {
    }

    public sealed record GitHub(string Url, string? Ref = null, string? Subpath = null, string? SkillFilter = null) : ParsedSource;

    public sealed record GitLab(string Url, string? Ref = null, string? Subpath = null) : ParsedSource;

    public sealed record Git(string Url, string? Ref = null) : ParsedSource;

    public sealed record Local(string Url, string LocalPath) : ParsedSource;

    public sealed record WellKnown(string Url) : ParsedSource;
}
