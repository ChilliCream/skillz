using Skillz.Sources;

namespace Skillz.Tests.TestServices;

internal sealed class TestSourceParser : ISourceParser
{
    public Func<string, SkillSource>? OnParse { get; set; }

    public SkillSource Parse(string input)
    {
        if (OnParse is not null)
        {
            return OnParse(input);
        }

        return SourceParser.ParseInternal(input);
    }
}
