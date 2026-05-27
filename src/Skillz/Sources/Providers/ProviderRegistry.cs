using Skillz.Skills;

namespace Skillz.Sources.Providers;

internal interface IProviderRegistry
{
    IProvider Resolve(ParsedSource source);

    IReadOnlyList<IProvider> Providers { get; }
}

internal sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlyList<IProvider> _providers;

    public ProviderRegistry(IEnumerable<IProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var list = providers.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in list)
        {
            if (!seen.Add(provider.Id))
            {
                throw new InvalidOperationException($"Provider with id \"{provider.Id}\" already registered.");
            }
        }

        _providers = list;
    }

    public IReadOnlyList<IProvider> Providers => _providers;

    public IProvider Resolve(ParsedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (var provider in _providers)
        {
            if (provider.CanHandle(source))
            {
                return provider;
            }
        }

        throw new InvalidOperationException($"No provider can handle ParsedSource of type {source.GetType().Name}.");
    }
}
