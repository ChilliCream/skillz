namespace Skillz.Sources;

internal static class SourceAliases
{
    private static readonly Dictionary<string, string> s_aliases = new(StringComparer.Ordinal)
    {
        ["coinbase/agentWallet"] = "coinbase/agentic-wallet-skills"
    };

    public static string Resolve(string input)
    {
        return s_aliases.TryGetValue(input, out var alias) ? alias : input;
    }
}
