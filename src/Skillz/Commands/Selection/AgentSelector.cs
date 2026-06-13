using System.Collections.Immutable;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Interaction.Decorators;
using Spectre.Console;

namespace Skillz.Commands.Selection;

internal sealed class AgentSelector(IAnsiConsole console, AgentRegistry registry) : IAgentSelector
{
    public async Task<ImmutableArray<string>> SelectAsync(
        ImmutableArray<string> available,
        ImmutableArray<string> defaults,
        CancellationToken cancellationToken)
    {
        if (available.Length == 0)
        {
            return [];
        }

        // Universal agents share .agents/skills, so installing to them is mandatory: their rows are
        // marked mandatory and noted. Rows are DisplayName-ordered, interleaving universals with the rest.
        var items = available
            .OrderBy(a => registry.GetConfig(a).DisplayName, StringComparer.Ordinal)
            .Select(a =>
            {
                var isUniversal = registry.IsUniversalAgent(a);
                return new SearchableItem<string>(
                    a,
                    $"{registry.GetConfig(a).DisplayName} ({a})",
                    Mandatory: isUniversal,
                    Note: isUniversal ? "universal · .agents" : null);
            })
            .ToImmutableArray();

        // Non-interactive fallback: every mandatory item unioned with the supplied defaults, in item order.
        var defaultSet = new HashSet<string>(defaults, StringComparer.Ordinal);
        var fallback = items
            .Where(item => item.Mandatory || defaultSet.Contains(item.Value))
            .Select(item => item.Value)
            .ToImmutableArray();

        // A non-interactive console (redirected input, CI, agent host) cannot drive the key loop, so
        // WithDefault returns the precomputed fallback instead of hanging.
        return await new SearchableMultiSelectionPrompt<string>(
                "Which agents do you want to install to?",
                items,
                defaults)
            .WithDefault(fallback)
            .ShowAsync(console, cancellationToken);
    }
}
