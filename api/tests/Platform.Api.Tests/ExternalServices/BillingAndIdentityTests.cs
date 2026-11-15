using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.ExternalServices;
using Platform.Core.External;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 7. Billing is manual in phase 1 in both environments, with no Stripe SDK (rule 10, D-020).
/// The Entra identity provider only maps already-validated claims to a PlatformIdentity (token validation is
/// US-109): subject from "oid" (short or long claim type), email from "email" or "preferred_username", display
/// name from "name", roles from "roles". A principal without a subject maps to null. The mock identity provider
/// is never registered in azure mode.
/// </summary>
public sealed class BillingAndIdentityTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LongObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    [Theory]
    [InlineData(DeploymentEnvironment.Local)]
    [InlineData(DeploymentEnvironment.Azure)]
    public void Billing_IsManual_InBothEnvironments(DeploymentEnvironment environment)
    {
        var settings = environment == DeploymentEnvironment.Local
            ? ExternalServicesHarness.LocalSettings()
            : ExternalServicesHarness.AzureSettings();
        using var provider = ExternalServicesHarness.Build(environment, settings);

        var billing = provider.GetServices<IBillingProvider>().ToList();

        var only = Assert.Single(billing);
        Assert.Equal("ManualBillingProvider", only.GetType().Name);
    }

    [Fact]
    public void NoStripeAssemblyLoaded()
    {
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<IBillingProvider>();
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .Where(name => name.StartsWith("Stripe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(loaded);
        Assert.Empty(StripeLibrariesInDependencyManifest());
    }

    [Theory]
    [InlineData("oid")]
    [InlineData(LongObjectIdClaim)]
    public void Entra_MapsClaimsToPlatformIdentity(string objectIdClaimType)
    {
        var entra = AzureIdentityProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(objectIdClaimType, "marker-object-id"),
                new Claim("preferred_username", "founder@example.test"),
                new Claim("name", "Marker Founder"),
                new Claim("roles", "admin"),
            ],
            authenticationType: "marker"));

        var identity = entra.FromClaims(principal);

        Assert.NotNull(identity);
        Assert.Equal("marker-object-id", identity.SubjectId);
        Assert.Equal("founder@example.test", identity.Email);
        Assert.Equal("Marker Founder", identity.DisplayName);
        Assert.Equal(new[] { "admin" }, identity.Roles);
    }

    [Fact]
    public void Entra_WithoutSubject_ReturnsNull()
    {
        var entra = AzureIdentityProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("name", "Marker Founder")],
            authenticationType: "marker"));

        Assert.Null(entra.FromClaims(principal));
        Assert.Null(entra.FromClaims(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void MockIdentity_NotRegistered_InAzure()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ExternalServicesHarness.AzureSettings())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExternalServices(DeploymentEnvironment.Azure, configuration);

        var mockDescriptors = services
            .Where(descriptor => (descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType())?.Name.Contains("Mock", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Empty(mockDescriptors);

        using var provider = services.BuildServiceProvider();
        Assert.All(
            provider.GetServices<IIdentityProvider>(),
            identity => Assert.DoesNotContain("Mock", identity.GetType().Name, StringComparison.Ordinal));
    }

    private static IIdentityProvider AzureIdentityProvider()
    {
        var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Azure, ExternalServicesHarness.AzureSettings());
        var identity = provider.GetRequiredService<IIdentityProvider>();
        Assert.Equal("EntraIdentityProvider", identity.GetType().Name);
        return identity;
    }

    private static List<string> StripeLibrariesInDependencyManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(BillingAndIdentityTests).Assembly.GetName().Name}.deps.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("libraries")
            .EnumerateObject()
            .Select(library => library.Name)
            .Where(name => name.StartsWith("Stripe", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
