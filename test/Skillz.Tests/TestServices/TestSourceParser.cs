using Skillz.Sources;

namespace Skillz.Tests.TestServices;

internal sealed class TestSourceParser : ISourceParser
{
    public Func<string, ParsedSource>? OnParse { get; set; }

    public ParsedSource Parse(string input)
    {
        if (OnParse is not null)
        {
            return OnParse(input);
        }

        return SourceParser.ParseInternal(input);
    }
}
