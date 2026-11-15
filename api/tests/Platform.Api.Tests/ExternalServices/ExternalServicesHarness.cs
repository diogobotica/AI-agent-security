using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Platform.Api.ExternalServices;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// Builds the external-service registrations (US-103) the way the API does, from settings only, so tests can
/// resolve the interfaces without starting the web host.
///
/// Contract the tests rely on:
/// <list type="bullet">
/// <item><c>Platform.Api.ExternalServices.ExternalServicesRegistration.AddExternalServices(this IServiceCollection,
/// DeploymentEnvironment, IConfiguration)</c> registers every external service.</item>
/// <item>Building and resolving makes no network call and reads no secret; clients connect on first use.</item>
/// <item>HTTP-based clients (model gateway, Key Vault transport) get their HttpClient from IHttpClientFactory, so a
/// test can replace the primary handler for every client.</item>
/// <item>Azure implementations take their credential from the <see cref="TokenCredential"/> registered in DI
/// (DefaultAzureCredential by default), so a test can replace it.</item>
/// <item>The local secret store reads configuration key <c>Secrets:&lt;name&gt;</c> (the variable SECRETS__&lt;name&gt;).</item>
/// </list>
/// All hosts are neutral: the reserved <c>.test</c> domain, a closed loopback port, or Azure-style names that no test
/// ever connects to.
/// </summary>
internal static class ExternalServicesHarness
{
    public const string BlobSecretName = "markerblob";
    public const string ServiceBusSecretName = "markerservicebus";
    public const string LocalModelEndpoint = "http://model-gateway.test/v1/";
    public const string MarkerModel = "marker-model";
    public const string VaultUri = "https://marker-vault.vault.azure.net/";

    /// <summary>The seven interfaces of US-103 and the implementation chosen for each environment.</summary>
    public static readonly (Type Service, string Local, string Azure)[] Implementations =
    [
        (typeof(Platform.Core.External.IQueue), "ServiceBusQueue", "ServiceBusQueue"),
        (typeof(Platform.Core.External.IEvidenceStore), "BlobEvidenceStore", "BlobEvidenceStore"),
        (typeof(Platform.Core.External.ISecretStore), "EnvironmentSecretStore", "KeyVaultSecretStore"),
        (typeof(Platform.Core.External.IModelGateway), "OpenAiCompatibleModelGateway", "OpenAiCompatibleModelGateway"),
        (typeof(Platform.Core.External.IIdentityProvider), "MockIdentityProvider", "EntraIdentityProvider"),
        (typeof(Platform.Core.External.IBillingProvider), "ManualBillingProvider", "ManualBillingProvider"),
        (typeof(Platform.Core.External.IClock), "SystemClock", "SystemClock"),
    ];

    /// <summary>Neutral base64 key material, built at run time (never a real key).</summary>
    public static string MarkerKey() => Convert.ToBase64String(Encoding.UTF8.GetBytes("neutral-marker-key-material-0001"));

    /// <summary>An Azurite-style connection string for the given blob endpoint.</summary>
    public static string BlobConnection(string blobEndpoint, string account, string key) =>
        string.Join(';', "DefaultEndpointsProtocol=http", "AccountName=" + account, "AccountKey=" + key, "BlobEndpoint=" + blobEndpoint) + ";";

    /// <summary>A Service Bus emulator-style connection string for the given host.</summary>
    public static string ServiceBusConnection(string host, string keyName, string key) =>
        string.Join(';', "Endpoint=sb://" + host, "SharedAccessKeyName=" + keyName, "SharedAccessKey=" + key, "UseDevelopmentEmulator=true") + ";";

    /// <summary>Local settings: connection strings point at a closed loopback port; the model at a reserved host.</summary>
    public static Dictionary<string, string?> LocalSettings() => new()
    {
        ["Services:Blob:ConnectionStringSecret"] = BlobSecretName,
        ["Services:ServiceBus:ConnectionStringSecret"] = ServiceBusSecretName,
        ["Secrets:" + BlobSecretName] = BlobConnection("http://127.0.0.1:1/markeraccount", "markeraccount", MarkerKey()),
        ["Secrets:" + ServiceBusSecretName] = ServiceBusConnection("127.0.0.1:1", "markerkeyname", MarkerKey()),
        ["ModelGateway:Endpoint"] = LocalModelEndpoint,
        ["ModelGateway:Model"] = MarkerModel,
    };

    /// <summary>Azure settings (every required key). Nothing ever connects to these names.</summary>
    public static Dictionary<string, string?> AzureSettings() => new()
    {
        ["Services:Blob:AccountUrl"] = "https://markeraccount.blob.core.windows.net/",
        ["Services:ServiceBus:Namespace"] = "marker-namespace.servicebus.windows.net",
        ["Services:KeyVault:Uri"] = VaultUri,
        ["ModelGateway:Endpoint"] = "https://marker-foundry.test/v1/",
        ["ModelGateway:Model"] = MarkerModel,
    };

    public static ServiceProvider Build(
        DeploymentEnvironment environment,
        IDictionary<string, string?> settings,
        Action<IServiceCollection>? afterRegistration = null,
        ILoggerProvider? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddExternalServices(environment, configuration);
        afterRegistration?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Replaces the primary handler of every HttpClient made by IHttpClientFactory.</summary>
    public static void UseHttpHandler(this IServiceCollection services, StubHttpHandler stub) =>
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = new ForwardingHandler(stub)));

    /// <summary>Wraps the shared stub so each client gets its own handler instance to dispose.</summary>
    private sealed class ForwardingHandler(StubHttpHandler stub) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            stub.HandleAsync(request, cancellationToken);
    }
}

/// <summary>Answers HTTP requests in memory and records them (no network).</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
{
    public ConcurrentQueue<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}

/// <summary>Collects every log message written through ILogger.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            sink.Enqueue($"{category} scope: {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink.Enqueue($"{category} {logLevel}: {formatter(state, exception)} {exception}");
        }
    }
}

/// <summary>A credential that hands out a neutral token and never contacts an identity service.</summary>
internal sealed class MarkerCredential : TokenCredential
{
    private int requests;

    public int Requests => requests;

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref requests);
        return new AccessToken("marker-access", FakeClock.DefaultStart.AddYears(10));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}
