namespace Platform.Core.External;

/// <summary>A customer as known to the billing system (in phase 1: the founder's manual invoicing).</summary>
public sealed record BillingCustomerRef(Guid TenantId, string Provider, string CustomerId);

/// <summary>
/// Billing. Phase 1 invoices by hand (Spec 05 §4, rule 10), so the only implementation is manual and creates
/// no external customer. Stripe arrives in phase C.
/// </summary>
public interface IBillingProvider
{
    /// <summary>The provider's name, for example "manual".</summary>
    string Name { get; }

    /// <summary>The billing customer for a tenant. The manual provider uses the tenant ID itself.</summary>
    Task<BillingCustomerRef> GetCustomerAsync(Guid tenantId, CancellationToken cancellationToken);
}
