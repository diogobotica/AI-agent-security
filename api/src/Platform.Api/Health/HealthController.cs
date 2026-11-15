using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Platform.Api.Health;

/// <summary>
/// Unversioned operational liveness endpoint. Returns status only: no version, environment or host details.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public ActionResult<HealthResponse> Get() => Ok(new HealthResponse("ok"));
}

/// <summary>Body of GET /health.</summary>
/// <param name="Status">Always "ok" when the API answers.</param>
public sealed record HealthResponse(string Status);
