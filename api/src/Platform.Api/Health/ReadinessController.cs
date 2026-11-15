using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Platform.Api.Health;

/// <summary>
/// Unversioned readiness endpoint: checks every dependency probe in parallel. 200 when all answer, otherwise 503
/// naming each unreachable dependency. The body never carries hosts, ports or error text; details go to the log.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("health/ready")]
public sealed class ReadinessController(
    IEnumerable<IDependencyProbe> probes,
    IOptions<DependencyOptions> options,
    ILogger<ReadinessController> logger) : ControllerBase
{
    /// <summary>Extra time the controller allows on top of the probe timeout before it stops waiting for a probe.</summary>
    private static readonly TimeSpan GuardMargin = TimeSpan.FromSeconds(1);

    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType<ReadinessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReadinessResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReadinessResponse>> Get(CancellationToken cancellationToken)
    {
        var guard = options.Value.ProbeTimeout + GuardMargin;
        var results = await Task.WhenAll(probes.Select(probe => CheckAsync(probe, guard, cancellationToken)));

        var checks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, reachable) in results)
        {
            // A name registered twice is reported unreachable if any of its probes fails.
            checks[name] = reachable && checks.GetValueOrDefault(name, "ok") == "ok" ? "ok" : "unreachable";
        }

        var ready = checks.Values.All(value => value == "ok");
        var body = new ReadinessResponse(ready ? "ready" : "not-ready", checks);
        return ready ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }

    private async Task<(string Name, bool Reachable)> CheckAsync(
        IDependencyProbe probe,
        TimeSpan guard,
        CancellationToken cancellationToken)
    {
        try
        {
            var reachable = await probe.CheckAsync(cancellationToken).WaitAsync(guard, cancellationToken);
            if (!reachable)
            {
                logger.LogWarning("Readiness check {Dependency} is unreachable", probe.Name);
            }

            return (probe.Name, reachable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A probe that throws or hangs counts as unreachable. Probe errors name hosts, never credentials.
            logger.LogWarning(exception, "Readiness check {Dependency} failed", probe.Name);
            return (probe.Name, false);
        }
    }
}

/// <summary>Body of GET /health/ready.</summary>
/// <param name="Status">"ready" or "not-ready".</param>
/// <param name="Checks">Dependency name to "ok" or "unreachable".</param>
public sealed record ReadinessResponse(string Status, IReadOnlyDictionary<string, string> Checks);
