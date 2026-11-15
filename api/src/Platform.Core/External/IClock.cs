namespace Platform.Core.External;

/// <summary>The current time. Inject it instead of reading DateTimeOffset.UtcNow, so tests control time.</summary>
public interface IClock
{
    /// <summary>The current time in UTC (offset zero).</summary>
    DateTimeOffset UtcNow { get; }
}
