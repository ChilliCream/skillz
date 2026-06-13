using System.Collections.Immutable;
using Skillz.Commands.Selection;

namespace Skillz.Tests.TestServices;

internal sealed class TestAgentSelector : IAgentSelector
{
    public Func<ImmutableArray<string>, IReadOnlyList<string>, IReadOnlyList<string>>? OnSelectAgents { get; set; }

    /// <summary>
    /// Captures the pre-selection handed to the most recent <see cref="SelectAsync"/> call, so the
    /// executor-tier tests can assert the defaults it computed from last-used persistence.
    /// </summary>
    public IReadOnlyList<string>? LastDefaults { get; private set; }

    public Task<ImmutableArray<string>> SelectAsync(
        ImmutableArray<string> available,
        ImmutableArray<string> defaults,
        CancellationToken cancellationToken)
    {
        LastDefaults = defaults;
        var result = OnSelectAgents is not null ? OnSelectAgents(available, defaults) : defaults;
        return Task.FromResult<ImmutableArray<string>>([.. result]);
    }
}
