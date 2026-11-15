using Azure;
using Azure.Security.KeyVault.Secrets;
using Platform.Core.External;

namespace Platform.Infrastructure.SecretStores;

/// <summary>
/// Secrets in Azure Key Vault (ENVIRONMENT=azure), reached with managed identity. Customer credentials live only here
/// (rule 4). Secret values are never logged, put in exception messages or shown by <see cref="ToString"/>; the SDK's
/// content logging stays off.
/// </summary>
public sealed class KeyVaultSecretStore(SecretClient client) : ISecretStore
{
    public async Task<string> GetSecretAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            var secret = await client.GetSecretAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
            return secret.Value.Value;
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            throw new KeyNotFoundException($"Secret '{name}' was not found in Key Vault.", error);
        }
    }

    public async Task SetSecretAsync(string name, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        await client.SetSecretAsync(name, value, cancellationToken).ConfigureAwait(false);
    }

    public override string ToString() => nameof(KeyVaultSecretStore);
}
