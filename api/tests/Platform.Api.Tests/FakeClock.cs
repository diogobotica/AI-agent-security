using Platform.Core.External;

namespace Platform.Api.Tests;

/// <summary>
/// Test clock (CLAUDE.md: inject IClock). Time moves only when a test sets or advances it.
/// </summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    /// <summary>A fixed, neutral start time for tests.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public FakeClock()
        : this(DefaultStart)
    {
    }

    public DateTimeOffset UtcNow { get; private set; } = start.ToUniversalTime();

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);

    public void Set(DateTimeOffset value) => UtcNow = value.ToUniversalTime();
}
