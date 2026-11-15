using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Platform.Core.External;

namespace Platform.Infrastructure.SecretStores;

/// <summary>
/// Local secret store (ENVIRONMENT=local). A secret named N is read from configuration key <c>Secrets:N</c>, which is
/// the environment variable <c>SECRETS__N</c> (from the git-ignored .env). Writes are kept in memory for the life of
/// the process only (D-020). Values are never logged, put in exception messages or shown by <see cref="ToString"/>.
/// </summary>
/// <remarks>
/// The folder is <c>SecretStores/</c>, not <c>Secrets/</c>: a folder named "secrets" is git-ignored and protected as
/// sensitive by the repository's guards.
/// </remarks>
public sealed class EnvironmentSecretStore(IConfiguration configuration) : ISecretStore
{
    private const string Section = "Secrets";

    private readonly ConcurrentDictionary<string, string> written = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> GetSecretAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (written.TryGetValue(name, out var value))
        {
            return Task.FromResult(value);
        }

        var configured = configuration[$"{Section}:{name}"];
        if (string.IsNullOrEmpty(configured))
        {
            throw new KeyNotFoundException($"Secret '{name}' was not found. Set the variable SECRETS__{name}.");
        }

        return Task.FromResult(configured);
    }

    public Task SetSecretAsync(string name, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        written[name] = value;
        return Task.CompletedTask;
    }

    public override string ToString() => nameof(EnvironmentSecretStore);
}
