namespace Platform.Core.External;

/// <summary>
/// Secret storage (Key Vault in Azure; environment variables locally). Implementations never log, return in
/// exceptions or show in ToString any secret value (rule 4).
/// </summary>
public interface ISecretStore
{
    /// <summary>Reads a secret. Throws <see cref="KeyNotFoundException"/> naming the secret if it does not exist.</summary>
    Task<string> GetSecretAsync(string name, CancellationToken cancellationToken);

    /// <summary>Creates or replaces a secret.</summary>
    Task SetSecretAsync(string name, string value, CancellationToken cancellationToken);
}
