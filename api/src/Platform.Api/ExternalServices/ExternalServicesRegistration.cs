using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Platform.Core.External;
using Platform.Infrastructure;
using Platform.Infrastructure.Billing;
using Platform.Infrastructure.Identity;
using Platform.Infrastructure.Secrets;

namespace Platform.Api.ExternalServices;

/// <summary>
/// The one composition root for external services (CLAUDE.md rule 2, ARC-07): ENVIRONMENT chooses each
/// implementation, so the local stack and Azure differ only in settings, never in code. Registering and resolving
/// makes no network call and reads no secret; clients connect on first use.
/// </summary>
public static class ExternalServicesRegistration
{
    public static IServiceCollection AddExternalServices(
        this IServiceCollection services,
        DeploymentEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient();

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
    }
}
