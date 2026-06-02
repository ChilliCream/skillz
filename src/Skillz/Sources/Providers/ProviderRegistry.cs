using System.Collections.Immutable;

namespace Skillz.Sources.Providers;

/// <summary>
/// Maintains the ordered set of <see cref="IProvider"/> instances and resolves the correct one
/// for a given <see cref="SkillSource"/>.
/// </summary>
internal sealed class ProviderRegistry
{
    /// <summary>
    /// Initializes a new <see cref="ProviderRegistry"/> with the supplied providers.
    /// </summary>
    /// <param name="providers">
    /// The providers to register. Each provider must have a unique <see cref="IProvider.Id"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Two or more providers share the same <see cref="IProvider.Id"/>.
    /// </exception>
    public ProviderRegistry(IEnumerable<IProvider> providers)
    {
        var list = providers.ToImmutableArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in list)
        {
            if (!seen.Add(provider.Id))
            {
                throw new InvalidOperationException($"Provider with id \"{provider.Id}\" already registered.");
            }
        }

        Providers = list;
    }

    /// <summary>
    /// Gets all registered providers in registration order.
    /// </summary>
    public ImmutableArray<IProvider> Providers { get; }

    /// <summary>
    /// Returns the first provider that can handle <paramref name="source"/>.
    /// </summary>
    /// <param name="source">
    /// The skill source to resolve a provider for.
    /// </param>
    /// <returns>
    /// The matching <see cref="IProvider"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No registered provider can handle <paramref name="source"/>.
    /// </exception>
    public IProvider Resolve(SkillSource source)
    {
        foreach (var provider in Providers)
        {
            if (provider.CanHandle(source))
            {
                return provider;
            }
        }

        throw new InvalidOperationException($"No provider can handle SkillSource of type {source.GetType().Name}.");
    }
}
