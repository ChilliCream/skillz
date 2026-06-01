using System.Collections.Immutable;

namespace Skillz.Sources.Providers;

internal interface IProviderRegistry
{
    IProvider Resolve(ParsedSource source);

    ImmutableArray<IProvider> Providers { get; }
}
