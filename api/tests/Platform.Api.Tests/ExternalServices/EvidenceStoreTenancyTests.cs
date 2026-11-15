using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Platform.Core.External;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 5 and rule 1: evidence names are {tenantId}/{runId}/{name}, built inside the store from the
/// caller's tenant ID. Reading a reference that belongs to another tenant fails with UnauthorizedAccessException,
/// and a name that could leave the run's folder is refused with ArgumentException, both before any request is sent.
/// The store here points at a closed loopback port, so a store that sent a request first would fail differently.
/// The round trip on Azurite is in Integration/LocalServicesTests.
/// </summary>
public sealed class EvidenceStoreTenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("0000000a-0000-4000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("0000000b-0000-4000-8000-00000000000b");
    private static readonly Guid Run = Guid.Parse("00000001-0000-4000-8000-000000000001");

    [Fact]
    public void EvidenceRef_PathStartsWithTenantId()
    {
        var reference = new EvidenceRef(TenantA, Run, "transcript.json");

        Assert.Equal($"{TenantA}/{Run}/transcript.json", reference.Path);
        Assert.StartsWith(TenantA.ToString(), reference.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_WithOtherTenant_Fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());
        var store = provider.GetRequiredService<IEvidenceStore>();
        var belongsToA = new EvidenceRef(TenantA, Run, "transcript.json");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetAsync(TenantB, belongsToA, cancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../transcript.json")]
    [InlineData("nested/transcript.json")]
    [InlineData("nested\\transcript.json")]
    [InlineData("..")]
    public async Task Put_WithNameOutsideTheRunFolder_Fails(string name)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());
        var store = provider.GetRequiredService<IEvidenceStore>();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("neutral evidence marker"));

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.PutAsync(TenantA, Run, name, content, "application/json", cancellationToken));
    }

    [Fact]
    public async Task Put_WithEmptyTenant_Fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, ExternalServicesHarness.LocalSettings());
        var store = provider.GetRequiredService<IEvidenceStore>();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("neutral evidence marker"));

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.PutAsync(Guid.Empty, Run, "transcript.json", content, "application/json", cancellationToken));
    }
}
