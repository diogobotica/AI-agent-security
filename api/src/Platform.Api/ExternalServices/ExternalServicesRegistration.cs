using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Platform.Core.External;
using Platform.Infrastructure;
using Platform.Infrastructure.Billing;
using Platform.Infrastructure.Identity;
using Platform.Infrastructure.Messaging;
using Platform.Infrastructure.ModelGateway;
using Platform.Infrastructure.SecretStores;
using Platform.Infrastructure.Storage;

namespace Platform.Api.ExternalServices;

/// <summary>
/// The one composition root for external services (CLAUDE.md rule 2, ARC-07): ENVIRONMENT chooses each
/// implementation, so the local stack and Azure differ only in settings, never in code. Registering and resolving
/// makes no network call and reads no secret; clients connect on first use.
/// </summary>
public static class ExternalServicesRegistration
{
    /// <summary>The named HttpClient behind the Key Vault client.</summary>
    public const string KeyVaultHttpClientName = "keyvault";

    /// <summary>The managed-identity scope for Azure AI Foundry (Azure OpenAI-compatible endpoints).</summary>
    private const string FoundryTokenScope = "https://cognitiveservices.azure.com/.default";

    public static IServiceCollection AddExternalServices(
        this IServiceCollection services,
        DeploymentEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient();
        services.AddHttpClient(OpenAiCompatibleModelGateway.HttpClientName);
        services.AddOptions<ModelGatewayOptions>().BindConfiguration(ModelGatewayOptions.Section);

        // Both environments.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IBillingProvider, ManualBillingProvider>();

        switch (environment)
        {
            case DeploymentEnvironment.Local:
                AddLocal(services);
                break;
            case DeploymentEnvironment.Azure:
                AddAzure(services);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unknown deployment environment.");
        }

        return services;
    }

    private static void AddLocal(IServiceCollection services)
    {
        services.AddSingleton<ISecretStore, EnvironmentSecretStore>();
        services.AddSingleton<IIdentityProvider, MockIdentityProvider>();

        // Azurite and the Service Bus emulator: connection strings come from the secret store on first use.
        services.AddSingleton(provider => BlobClientFactory.FromSecret(
            provider.GetRequiredService<ISecretStore>(),
            Setting(provider, ExternalServicesOptions.BlobConnectionSecretKey),
            ExternalServicesOptions.BlobConnectionSecretKey,
            BlobContainer(provider)));
        services.AddSingleton(provider => ServiceBusClientFactory.FromSecret(
            provider.GetRequiredService<ISecretStore>(),
            Setting(provider, ExternalServicesOptions.ServiceBusConnectionSecretKey),
            ExternalServicesOptions.ServiceBusConnectionSecretKey));
        AddStorageAndQueue(services);

        // Ollama: no Authorization header.
        services.AddSingleton<IModelGateway>(provider => new OpenAiCompatibleModelGateway(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<ModelGatewayOptions>>()));
    }

    private static void AddStorageAndQueue(IServiceCollection services)
    {
        services.AddSingleton<IEvidenceStore, BlobEvidenceStore>();
        services.AddSingleton<IQueue, ServiceBusQueue>();
    }

    private static string? Setting(IServiceProvider provider, string key) =>
        provider.GetRequiredService<IConfiguration>()[key];

    private static string BlobContainer(IServiceProvider provider)
    {
        var configured = Setting(provider, ExternalServicesOptions.BlobContainerKey);
        return string.IsNullOrWhiteSpace(configured) ? ExternalServicesOptions.DefaultBlobContainer : configured;
    }

    private static void AddAzure(IServiceCollection services)
    {
        // Every required setting is checked when the host starts; a missing one stops startup naming the key.
        services.AddOptions<ExternalServicesOptions>()
            .Configure<IConfiguration>((options, settings) =>
            {
                foreach (var key in ExternalServicesOptions.RequiredInAzure)
                {
                    options.Values[key] = settings[key];
                }
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalServicesOptions>, AzureSettingsValidator>();

        // Managed identity in Azure. Created on first use; tests replace it with their own TokenCredential.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        services.AddSingleton<IIdentityProvider, EntraIdentityProvider>();

        // Blob Storage and Service Bus with managed identity; no connection strings in Azure.
        services.AddSingleton(provider => BlobClientFactory.FromIdentity(
            Setting(provider, ExternalServicesOptions.BlobAccountUrlKey),
            ExternalServicesOptions.BlobAccountUrlKey,
            provider.GetRequiredService<TokenCredential>(),
            BlobContainer(provider)));
        services.AddSingleton(provider => ServiceBusClientFactory.FromIdentity(
            Setting(provider, ExternalServicesOptions.ServiceBusNamespaceKey),
            ExternalServicesOptions.ServiceBusNamespaceKey,
            provider.GetRequiredService<TokenCredential>()));
        AddStorageAndQueue(services);

        // Key Vault with managed identity. The HttpClient comes from IHttpClientFactory; no request is made until a
        // secret is read or written.
        services.AddSingleton(provider =>
        {
            var vault = Setting(provider, ExternalServicesOptions.KeyVaultUriKey);
            if (!Uri.TryCreate(vault, UriKind.Absolute, out var vaultUri))
            {
                throw new InvalidOperationException($"Setting {ExternalServicesOptions.KeyVaultUriKey} must be an absolute URL.");
            }

            var transport = new HttpClientTransport(provider.GetRequiredService<IHttpClientFactory>().CreateClient(KeyVaultHttpClientName));
            return new SecretClient(vaultUri, provider.GetRequiredService<TokenCredential>(), new SecretClientOptions { Transport = transport });
        });
        services.AddHttpClient(KeyVaultHttpClientName);
        services.AddSingleton<ISecretStore, KeyVaultSecretStore>();

        // Azure AI Foundry with a managed-identity token.
        services.PostConfigure<ModelGatewayOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.TokenScope))
            {
                options.TokenScope = FoundryTokenScope;
            }
        });
        services.AddSingleton<IModelGateway>(provider => new OpenAiCompatibleModelGateway(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<ModelGatewayOptions>>(),
            provider.GetRequiredService<TokenCredential>()));
    }
}
