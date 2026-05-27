using Xunit;

namespace Skillz.Tests.Commands;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommandTestCollection
{
    public const string Name = "Command Tests";
}
