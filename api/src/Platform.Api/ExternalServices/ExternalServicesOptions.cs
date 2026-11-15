using Microsoft.Extensions.Options;

namespace Platform.Api.ExternalServices;

/// <summary>
/// The settings the external services need in Azure (ENVIRONMENT=azure). They are names and addresses, never
/// secrets: Azure clients authenticate with managed identity. Locally the emulators' connection strings come from
/// the secret store instead (Services:*:ConnectionStringSecret).
/// </summary>
public sealed class ExternalServicesOptions
{
    public const string BlobAccountUrlKey = "Services:Blob:AccountUrl";
    public const string ServiceBusNamespaceKey = "Services:ServiceBus:Namespace";
    public const string KeyVaultUriKey = "Services:KeyVault:Uri";
    public const string ModelEndpointKey = "ModelGateway:Endpoint";
    public const string ModelKey = "ModelGateway:Model";

    // Local only: the names (not the values) of the secrets holding the emulators' connection strings.
    public const string BlobConnectionSecretKey = "Services:Blob:ConnectionStringSecret";
    public const string ServiceBusConnectionSecretKey = "Services:ServiceBus:ConnectionStringSecret";

    // Both environments, with a default.
    public const string BlobContainerKey = "Services:Blob:Container";
    public const string DefaultBlobContainer = "evidence";

    /// <summary>The keys that must have a value in Azure, in the order they are reported.</summary>
    public static readonly IReadOnlyList<string> RequiredInAzure =
        [BlobAccountUrlKey, ServiceBusNamespaceKey, KeyVaultUriKey, ModelEndpointKey, ModelKey];

    /// <summary>Configured values by key, read from configuration at startup.</summary>
    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string key] => Values.GetValueOrDefault(key);
}

/// <summary>
/// Stops startup in Azure when a required setting is missing or empty. The message names each missing key (in both
/// the <c>:</c> and the environment-variable <c>__</c> form) and never shows any configured value.
/// </summary>
internal sealed class AzureSettingsValidator : IValidateOptions<ExternalServicesOptions>
{
    public ValidateOptionsResult Validate(string? name, ExternalServicesOptions options)
    {
        var missing = ExternalServicesOptions.RequiredInAzure
            .Where(key => string.IsNullOrWhiteSpace(options[key]))
            .Select(key => $"Missing setting {key} (environment variable {key.Replace(":", "__", StringComparison.Ordinal)}).")
            .ToList();
        return missing.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(missing);
    }
}
