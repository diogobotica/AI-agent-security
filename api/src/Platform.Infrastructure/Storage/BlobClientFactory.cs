using Azure.Core;
using Azure.Storage.Blobs;
using Platform.Core.External;

namespace Platform.Infrastructure.Storage;

/// <summary>
/// Creates the evidence container client on first use, never at registration, so resolving the store reads no secret
/// and makes no network call. Locally the connection string (Azurite) comes from <see cref="ISecretStore"/>; in Azure
/// the account URL is used with the managed-identity credential.
/// </summary>
public sealed class BlobClientFactory : IDisposable
{
    private readonly Func<CancellationToken, Task<BlobServiceClient>> createService;
    private readonly string containerName;
    private readonly SemaphoreSlim gate = new(1, 1);
    private BlobContainerClient? container;

    private BlobClientFactory(Func<CancellationToken, Task<BlobServiceClient>> createService, string containerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        this.createService = createService;
        this.containerName = containerName;
    }

    /// <summary>Local: the connection string is the secret named <paramref name="secretName"/>.</summary>
    public static BlobClientFactory FromSecret(ISecretStore secrets, string? secretName, string settingKey, string containerName)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        return new BlobClientFactory(
            async cancellationToken =>
            {
                if (string.IsNullOrWhiteSpace(secretName))
                {
                    throw new InvalidOperationException($"Missing setting {settingKey}.");
                }

                return new BlobServiceClient(await secrets.GetSecretAsync(secretName, cancellationToken).ConfigureAwait(false));
            },
            containerName);
    }

    /// <summary>Azure: the storage account URL with the managed-identity credential.</summary>
    public static BlobClientFactory FromIdentity(string? accountUrl, string settingKey, TokenCredential credential, string containerName)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new BlobClientFactory(
            _ => Uri.TryCreate(accountUrl, UriKind.Absolute, out var uri)
                ? Task.FromResult(new BlobServiceClient(uri, credential))
                : throw new InvalidOperationException($"Setting {settingKey} must be an absolute URL."),
            containerName);
    }

    /// <summary>The evidence container, created if it does not exist yet (once per process).</summary>
    public async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (container is not null)
        {
            return container;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (container is null)
            {
                var service = await createService(cancellationToken).ConfigureAwait(false);
                var client = service.GetBlobContainerClient(containerName);
                await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                container = client;
            }

            return container;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    public override string ToString() => $"{nameof(BlobClientFactory)}({containerName})";
}
