using Azure;
using Azure.Storage.Blobs.Models;
using Platform.Core.External;

namespace Platform.Infrastructure.Storage;

/// <summary>
/// Evidence in Blob Storage (Azurite locally). Blob names are always <c>{tenantId}/{runId}/{name}</c>, built here and
/// never by callers (rule 1). Reading another tenant's reference fails before any request. Evidence is never
/// overwritten: a second write to the same name fails.
/// </summary>
public sealed class BlobEvidenceStore(BlobClientFactory clients) : IEvidenceStore
{
    public async Task<EvidenceRef> PutAsync(
        Guid tenantId,
        Guid runId,
        string name,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var reference = new EvidenceRef(tenantId, runId, name);
        Validate(reference);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var container = await clients.GetContainerAsync(cancellationToken).ConfigureAwait(false);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };
        await container.GetBlobClient(reference.Path).UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
        return reference;
    }

    public async Task<Stream> GetAsync(Guid tenantId, EvidenceRef reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (tenantId == Guid.Empty || reference.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException("The evidence does not belong to this tenant.");
        }

        Validate(reference);
        var container = await clients.GetContainerAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var download = await container.GetBlobClient(reference.Path)
                .DownloadStreamingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return download.Value.Content;
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            throw new FileNotFoundException("The evidence was not found.", reference.Path, error);
        }
    }

    public override string ToString() => nameof(BlobEvidenceStore);

    /// <summary>A tenant and run are required, and the name must stay inside the run folder.</summary>
    private static void Validate(EvidenceRef reference)
    {
        if (reference.TenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant ID is required.", nameof(reference));
        }

        if (reference.RunId == Guid.Empty)
        {
            throw new ArgumentException("A run ID is required.", nameof(reference));
        }

        var name = reference.Name;
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains('/', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal)
            || name.Contains("..", StringComparison.Ordinal)
            || name.Any(char.IsControl))
        {
            throw new ArgumentException("The evidence name must be a single file name inside the run folder.", nameof(reference));
        }
    }
}
