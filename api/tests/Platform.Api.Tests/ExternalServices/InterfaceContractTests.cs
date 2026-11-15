using Platform.Core;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 1 and rule 2: every external service sits behind a public interface in Platform.Core
/// (namespace Platform.Core.External). That Core still references no Azure or Stripe SDK is checked by
/// <see cref="ArchitectureRulesTests.PlatformCore_ReferencesNoAzureOrStripeSdk"/>.
/// </summary>
public sealed class InterfaceContractTests
{
    private static readonly string[] Interfaces =
    [
        "IQueue",
        "IEvidenceStore",
        "ISecretStore",
        "IModelGateway",
        "IIdentityProvider",
        "IBillingProvider",
        "IClock",
    ];

    [Fact]
    public void CoreDefinesEveryExternalServiceInterface()
    {
        var core = typeof(CoreAssemblyMarker).Assembly;

        var missing = Interfaces
            .Where(name => core.GetType($"Platform.Core.External.{name}") is not { IsInterface: true, IsPublic: true })
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void FakeClock_MovesOnlyWhenTold()
    {
        var clock = new FakeClock();
        var start = clock.UtcNow;

        Assert.Equal(start, clock.UtcNow);
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(start.AddMinutes(5), clock.UtcNow);
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }
}
