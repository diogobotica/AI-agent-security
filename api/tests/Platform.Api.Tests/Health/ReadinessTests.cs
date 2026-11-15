using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Health;

namespace Platform.Api.Tests.Health;

/// <summary>
/// US-102: GET /health/ready reports whether the API reaches each local dependency (ENVIRONMENT=local, set for the
/// test process by LocalEnvironmentFixture). The probes are replaced with in-memory fakes, so no network is used.
///
/// Contract: <see cref="IDependencyProbe"/> has <c>string Name</c> and <c>Task&lt;bool&gt; CheckAsync(CancellationToken)</c>;
/// the controller uses every registered probe. 200 {"status":"ready","checks":{name:"ok",…}} when all succeed,
/// otherwise 503 {"status":"not-ready","checks":{…}} with "unreachable" for each failing probe. A probe that
/// throws counts as unreachable. The body never carries hosts, ports or error text.
/// </summary>
public sealed class ReadinessTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ReadyPath = "/health/ready";
    private const string NeutralHost = "marker-host.invalid";
    private const string NeutralPort = "6543";
    private static readonly string[] Dependencies = ["postgres", "blob", "servicebus", "ollama"];

    [Fact]
    public async Task Ready_WhenAllProbesSucceed_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = WithProbes(Dependencies.Select(name => FakeProbe.Reachable(name)).ToArray());
        using var client = app.CreateClient();

        using var response = await client.GetAsync(ReadyPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var (status, checks) = await ReadBody(response, cancellationToken);
        Assert.Equal("ready", status);
        Assert.Equal(Dependencies.ToDictionary(name => name, _ => "ok"), checks);
    }

    [Fact]
    public async Task NotReady_WhenOneProbeFails_Returns503NamingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = WithProbes(
            FakeProbe.Reachable("postgres"),
            FakeProbe.Reachable("blob"),
            FakeProbe.Unreachable("servicebus"),
            FakeProbe.Reachable("ollama"));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(ReadyPath, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var (status, checks) = await ReadBody(response, cancellationToken);
        Assert.Equal("not-ready", status);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["postgres"] = "ok",
                ["blob"] = "ok",
                ["servicebus"] = "unreachable",
                ["ollama"] = "ok",
            },
            checks);
    }

    [Fact]
    public async Task Readiness_NeverRevealsHostsPortsOrErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = WithProbes(
            FakeProbe.Throwing("postgres", $"Connection refused by {NeutralHost}:{NeutralPort}"),
            FakeProbe.Unreachable("blob"),
            FakeProbe.Reachable("servicebus"),
            FakeProbe.Reachable("ollama"));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(ReadyPath, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var detail in new[] { NeutralHost, NeutralPort, "refused", "Exception", "   at " })
        {
            Assert.DoesNotContain(detail, body, StringComparison.OrdinalIgnoreCase);
        }

        var (_, checks) = await ReadBody(response, cancellationToken);
        Assert.Equal("unreachable", checks["postgres"]);
        Assert.Equal("unreachable", checks["blob"]);
    }

    [Fact]
    public void LocalEnvironment_RegistersAProbeForEveryDependency()
    {
        // The test process runs with ENVIRONMENT=local (LocalEnvironmentFixture). Probes are only resolved, never run.
        using var scope = factory.Services.CreateScope();
        var names = scope.ServiceProvider.GetServices<IDependencyProbe>().Select(probe => probe.Name).Order().ToList();

        Assert.Equal(Dependencies.Order().ToList(), names);
    }

    [Fact]
    public void ReadinessController_IsAnAnonymousApiController()
    {
        var controller = typeof(ReadinessController);

        Assert.True(typeof(ControllerBase).IsAssignableFrom(controller), "ReadinessController must derive from ControllerBase.");
        Assert.True(controller.IsDefined(typeof(ApiControllerAttribute), inherit: true), "ReadinessController must carry [ApiController].");
        Assert.True(
            controller.IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
            "ReadinessController must carry [AllowAnonymous] so readiness stays open once US-109 adds authentication.");
    }

    private WebApplicationFactory<Program> WithProbes(params IDependencyProbe[] probes) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDependencyProbe>();
                foreach (var probe in probes)
                {
                    services.AddSingleton(probe);
                }
            });
        });

    private static async Task<(string? Status, Dictionary<string, string> Checks)> ReadBody(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var status = root.GetProperty("status").GetString();
        var checks = root.GetProperty("checks")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
        return (status, checks);
    }

    private sealed class FakeProbe(string name, Func<Task<bool>> check) : IDependencyProbe
    {
        public string Name => name;

        public Task<bool> CheckAsync(CancellationToken cancellationToken) => check();

        public static FakeProbe Reachable(string name) => new(name, () => Task.FromResult(true));

        public static FakeProbe Unreachable(string name) => new(name, () => Task.FromResult(false));

        public static FakeProbe Throwing(string name, string message) =>
            new(name, () => Task.FromException<bool>(new InvalidOperationException(message)));
    }
}
