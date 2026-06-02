namespace Skillz.Sources;

internal abstract record SkillSource
{
    private SkillSource() { }

    public abstract string Url { get; init; }

    public abstract string? Ref { get; init; }

    public abstract string SourceType { get; }

    public string DisplayString
    {
        get
        {
            var url = Url;
            if (!string.IsNullOrEmpty(Ref))
            {
                url += $" @ {Ref}";
            }
            if (this is GitHub { Subpath: { Length: > 0 } subpath })
            {
                url += $" ({subpath})";
            }
            return url;
        }
    }

    public sealed record GitHub(string Url, string? Ref = null, string? Subpath = null, string? SkillFilter = null)
        : SkillSource
        , ISkillFilterable
    {
        public override string SourceType => "github";
    }

    public sealed record GitLab(string Url, string? Ref = null, string? Subpath = null) : SkillSource
    {
        public override string SourceType => "gitlab";
    }

    public sealed record Git(string Url, string? Ref = null) : SkillSource
    {
        public override string SourceType => "git";
    }

    public sealed record Local(string Url, string LocalPath) : SkillSource
    {
        public override string? Ref
        {
            get => null;
            init { }
        }

        public override string SourceType => "local";
    }

    public sealed record WellKnown(string Url) : SkillSource
    {
        public override string? Ref
        {
            get => null;
            init { }
        }

        public override string SourceType => "well-known";
    }

    public interface ISkillFilterable
    {
        string? SkillFilter { get; init; }
    }
}
