namespace Platform.Api.Health;

/// <summary>Checks whether the API can reach one external dependency. Used by GET /health/ready.</summary>
public interface IDependencyProbe
{
    /// <summary>Short dependency name shown in the readiness response (for example "postgres").</summary>
    string Name { get; }

    /// <summary>
    /// Returns true when the dependency answers. Returns false, and does not throw, when it is unreachable, and gives
    /// up after the probe's own timeout. Creating a probe never connects; only this call does.
    /// </summary>
    Task<bool> CheckAsync(CancellationToken cancellationToken);
}
