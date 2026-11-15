using Azure.Core;
using Azure.Messaging.ServiceBus;
using Platform.Core.External;

namespace Platform.Infrastructure.Messaging;

/// <summary>
/// Creates the Service Bus client on first use, never at registration. Locally the connection string (emulator) comes
/// from <see cref="ISecretStore"/>; in Azure the fully qualified namespace is used with the managed-identity credential.
/// </summary>
public sealed class ServiceBusClientFactory : IAsyncDisposable, IDisposable
{
    private readonly Func<CancellationToken, Task<ServiceBusClient>> createClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ServiceBusClient? client;

    private ServiceBusClientFactory(Func<CancellationToken, Task<ServiceBusClient>> createClient) => this.createClient = createClient;

    /// <summary>Local: the connection string is the secret named <paramref name="secretName"/>.</summary>
    public static ServiceBusClientFactory FromSecret(ISecretStore secrets, string? secretName, string settingKey)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        return new ServiceBusClientFactory(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(secretName))
            {
                throw new InvalidOperationException($"Missing setting {settingKey}.");
            }

            return new ServiceBusClient(await secrets.GetSecretAsync(secretName, cancellationToken).ConfigureAwait(false));
        });
    }

    /// <summary>Azure: the fully qualified namespace with the managed-identity credential.</summary>
    public static ServiceBusClientFactory FromIdentity(string? fullyQualifiedNamespace, string settingKey, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new ServiceBusClientFactory(_ => string.IsNullOrWhiteSpace(fullyQualifiedNamespace)
            ? throw new InvalidOperationException($"Missing setting {settingKey}.")
            : Task.FromResult(new ServiceBusClient(fullyQualifiedNamespace, credential)));
    }

    public async Task<ServiceBusClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            return client;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            client ??= await createClient(cancellationToken).ConfigureAwait(false);
            return client;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }

        gate.Dispose();
    }

    /// <summary>For containers disposed synchronously; closes the client (and its senders and receivers).</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public override string ToString() => nameof(ServiceBusClientFactory);
}
