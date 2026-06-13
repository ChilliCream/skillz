using System.Collections.Immutable;

namespace Skillz.Commands.Selection;

/// <summary>
/// Chooses which agents to install to, applying the partition policy: universal agents are
/// mandatory (they share <c>.agents/skills</c>), the rest are optional, and the list is presented
/// in display-name order. Pure - the caller supplies the pre-selection and persists the result;
/// the selector only drives the prompt. Injected so command flow can be tested without a TTY.
/// </summary>
internal interface IAgentSelector
{
    Task<ImmutableArray<string>> SelectAsync(
        ImmutableArray<string> available,
        ImmutableArray<string> defaults,
        CancellationToken cancellationToken);
}
