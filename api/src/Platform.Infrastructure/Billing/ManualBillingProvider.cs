using Platform.Core.External;

namespace Platform.Infrastructure.Billing;

/// <summary>
/// Phase 1 billing: the founder invoices by hand (Spec 05 §4), so there is no external billing system and no Stripe
/// SDK (rule 10). The billing customer is the tenant itself.
/// </summary>
public sealed class ManualBillingProvider : IBillingProvider
{
    public const string ProviderName = "manual";

    public string Name => ProviderName;

    public Task<BillingCustomerRef> GetCustomerAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BillingCustomerRef(tenantId, ProviderName, tenantId.ToString()));
    }
}
