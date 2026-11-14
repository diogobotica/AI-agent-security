using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Api.Health;

namespace Platform.Api.Tests.Health;

/// <summary>
/// US-101: GET /health on the API answers 200 with exactly {"status":"ok"}, without authentication.
/// Runs the API in process through WebApplicationFactory (TestServer): no network.
/// </summary>
public sealed class HealthControllerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HealthPath = "/health";

    [Fact]
    public async Task GetHealth_Returns200WithStatusOkOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal("""{"status":"ok"}""", body);
    }

    [Fact]
    public async Task GetHealth_SucceedsWithoutAuthentication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var controller = typeof(HealthController);
        var getActions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.IsDefined(typeof(HttpGetAttribute), inherit: true))
            .ToList();
        Assert.NotEmpty(getActions);

        // [AllowAnonymous] keeps /health open once US-109 adds a fallback authorization policy.
        var anonymousOnController = controller.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);
        var anonymousOnEveryGet = getActions.All(
            method => method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.True(
            anonymousOnController || anonymousOnEveryGet,
            "HealthController (or its GET action) must carry [AllowAnonymous].");

        using var client = factory.CreateClient();
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        using var response = await client.GetAsync(HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_DoesNotSendServerHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // TestServer never writes a Server header itself, so the Kestrel setting that controls it is checked too.
        var kestrelOptions = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.False(
            kestrelOptions.AddServerHeader,
            "Kestrel must be configured with AddServerHeader = false so responses do not reveal the server.");

        using var client = factory.CreateClient();
        using var response = await client.GetAsync(HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.Server);
    }

    [Fact]
    public void HealthController_IsAnApiController()
    {
        var controller = typeof(HealthController);

        Assert.True(
            typeof(ControllerBase).IsAssignableFrom(controller),
            "HealthController must derive from ControllerBase (ASP.NET Core controllers, not minimal APIs).");
        Assert.True(
            controller.IsDefined(typeof(ApiControllerAttribute), inherit: true),
            "HealthController must carry [ApiController].");
    }
}
