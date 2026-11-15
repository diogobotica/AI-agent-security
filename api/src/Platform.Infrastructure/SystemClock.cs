using Platform.Core.External;

namespace Platform.Infrastructure;

/// <summary>The real clock, in UTC.</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public SystemClock()
        : this(TimeProvider.System)
    {
    }

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
