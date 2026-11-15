using Microsoft.Extensions.DependencyInjection;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 2: ENVIRONMENT chooses the implementation of every external service. One test per interface
/// and environment. Implementations are named, not referenced, so these tests compile before every
/// implementation exists (Queue and evidence: T3; Key Vault and the model gateway: T4).
/// </summary>
public sealed class ServiceSelectionTests
{
    public static TheoryData<string> ServiceNames()
    {
        var data = new TheoryData<string>();
        foreach (var (service, _, _) in ExternalServicesHarness.Implementations)
        {
            data.Add(service.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ServiceNames))]
    public void Local_RegistersLocalImplementation(string serviceName)
    {
        var (service, local, _) = ExternalServicesHarness.Implementations.Single(row => row.Service.Name == serviceName);
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());
        using var scope = provider.CreateScope();

        var implementation = scope.ServiceProvider.GetService(service);

        Assert.NotNull(implementation);
        Assert.Equal(local, implementation.GetType().Name);
    }

    [Theory]
    [MemberData(nameof(ServiceNames))]
    public void Azure_RegistersAzureImplementation(string serviceName)
    {
        var (service, _, azure) = ExternalServicesHarness.Implementations.Single(row => row.Service.Name == serviceName);
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Azure, ExternalServicesHarness.AzureSettings());
        using var scope = provider.CreateScope();

        var implementation = scope.ServiceProvider.GetService(service);

        Assert.NotNull(implementation);
        Assert.Equal(azure, implementation.GetType().Name);
    }

    [Fact]
    public void Clock_ReturnsUtc()
    {
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());

        var clock = provider.GetRequiredService<Platform.Core.External.IClock>();

        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }
}
