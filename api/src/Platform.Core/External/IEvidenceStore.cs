namespace Platform.Core.External;

/// <summary>A stored piece of evidence. Its path always starts with the tenant ID (rule 1).</summary>
public sealed record EvidenceRef(Guid TenantId, Guid RunId, string Name)
{
    /// <summary>The store path: <c>{TenantId}/{RunId}/{Name}</c>.</summary>
    public string Path => $"{TenantId}/{RunId}/{Name}";
}

/// <summary>Evidence storage (Blob; Azurite locally). Paths are built inside the store, never by callers.</summary>
public interface IEvidenceStore
{
    /// <summary>
    /// Stores evidence under <c>{tenantId}/{runId}/{name}</c>. Throws <see cref="ArgumentException"/> for an empty
    /// tenant or a name that is empty or leaves the run folder (<c>/</c>, <c>\</c>, <c>..</c>).
    /// </summary>
    Task<EvidenceRef> PutAsync(Guid tenantId, Guid runId, string name, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Reads evidence for <paramref name="tenantId"/>. Throws <see cref="UnauthorizedAccessException"/> if the
    /// reference belongs to another tenant, before any request is made.
    /// </summary>
    Task<Stream> GetAsync(Guid tenantId, EvidenceRef reference, CancellationToken cancellationToken);
}
