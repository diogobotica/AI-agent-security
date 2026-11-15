using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Core.External;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 4: the local secret store reads secrets from the environment. A secret named N comes from the
/// variable SECRETS__N (configuration key Secrets:N). A missing secret throws KeyNotFoundException naming the
/// secret. Writes are kept in memory only (D-020). These tests set process variables, so they run alone.
/// </summary>
[Collection(nameof(ProcessEnvironmentCollection))]
public sealed class EnvironmentSecretStoreTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SecretName = "markerenvsecret";

    [Fact]
    public async Task EnvironmentSecretStore_ReadsSecret()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = "marker-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("SECRETS__" + SecretName, expected);
        try
        {
            using var app = factory.WithWebHostBuilder(_ => { });
            using var scope = app.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();

            var actual = await store.GetSecretAsync(SecretName, cancellationToken);

            Assert.Equal(expected, actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SECRETS__" + SecretName, null);
        }
    }

    [Fact]
    public async Task EnvironmentSecretStore_MissingSecret_ThrowsKeyNotFoundNamingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());
        var store = provider.GetRequiredService<ISecretStore>();

        var error = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => store.GetSecretAsync("markermissing", cancellationToken));

        Assert.Contains("markermissing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvironmentSecretStore_KeepsWritesInMemory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var written = "marker-" + Guid.NewGuid().ToString("N");
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());
        var store = provider.GetRequiredService<ISecretStore>();

        await store.SetSecretAsync("markerwritten", written, cancellationToken);
        var readBack = await store.GetSecretAsync("markerwritten", cancellationToken);

        Assert.Equal(written, readBack);
        Assert.Null(Environment.GetEnvironmentVariable("SECRETS__markerwritten"));
    }
}
