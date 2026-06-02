using System.Collections.Immutable;
using Skillz.Skills;
using Skillz.Sources;
using Skillz.Sources.Providers;
using Xunit;

namespace Skillz.Tests.Sources.Providers;

public class ProviderRegistryTests
{
    [Fact]
    public void Constructor_Should_Throw_When_Duplicate_Id_Is_Registered()
    {
        // Arrange
        var providers = new IProvider[] { new StubProvider("a"), new StubProvider("a") };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new ProviderRegistry(providers));
    }

    [Fact]
    public void Providers_Should_Return_All_Registered_Providers()
    {
        // Arrange
        var a = new StubProvider("a");
        var b = new StubProvider("b");
        var registry = new ProviderRegistry([a, b]);

        // Act
        var result = registry.Providers;

        // Assert
        Assert.Equal([a, b], result);
    }

    [Fact]
    public void Resolve_Should_Return_First_Matching_Provider()
    {
        // Arrange
        var source = new SkillSource.Local("/tmp/x", "/tmp/x");
        var match = new StubProvider("match", canHandle: true);
        var other = new StubProvider("other", canHandle: true);
        var registry = new ProviderRegistry([match, other]);

        // Act
        var result = registry.Resolve(source);

        // Assert
        Assert.Same(match, result);
    }

    [Fact]
    public void Resolve_Should_Skip_Non_Matching_Providers()
    {
        // Arrange
        var source = new SkillSource.Local("/tmp/x", "/tmp/x");
        var skip = new StubProvider("skip", canHandle: false);
        var match = new StubProvider("match", canHandle: true);
        var registry = new ProviderRegistry([skip, match]);

        // Act
        var result = registry.Resolve(source);

        // Assert
        Assert.Same(match, result);
    }

    [Fact]
    public void Resolve_Should_Throw_When_No_Provider_Matches()
    {
        // Arrange
        var source = new SkillSource.Local("/tmp/x", "/tmp/x");
        var registry = new ProviderRegistry([new StubProvider("none", canHandle: false)]);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => registry.Resolve(source));
    }

    [Fact]
    public void Resolve_Should_Throw_When_No_Providers_Registered()
    {
        // Arrange
        var registry = new ProviderRegistry([]);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => registry.Resolve(new SkillSource.Local("/tmp/x", "/tmp/x")));
    }

    private sealed class StubProvider(string id, bool canHandle = false) : IProvider
    {
        public string Id => id;

        public bool CanHandle(SkillSource source) => canHandle;

        public Task<ImmutableArray<ResolvedSkill>> FetchSkillsAsync(
            SkillSource source,
            ProviderOptions? options,
            CancellationToken cancellationToken)
            => Task.FromResult(ImmutableArray<ResolvedSkill>.Empty);
    }
}
