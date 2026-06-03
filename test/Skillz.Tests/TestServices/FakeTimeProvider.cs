namespace Skillz.Tests.TestServices;

/// <summary>
/// A <see cref="TimeProvider"/> whose current time is fixed at construction and can be
/// advanced by reassigning <see cref="UtcNow"/>, so tests get deterministic timestamps.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
