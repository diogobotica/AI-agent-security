using Microsoft.Extensions.Options;

namespace Platform.Api.Health;

/// <summary>Registers the readiness probes for the configured <see cref="DeploymentEnvironment"/>.</summary>
public static class ReadinessServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient used by the HTTP probes (no proxy, no redirects).</summary>
    public const string HttpClientName = "readiness";

    private static readonly string[] DependencyNames = ["postgres", "blob", "servicebus", "ollama"];

    /// <summary>
    /// Local: one probe per emulated dependency, with addresses from <see cref="DependencyOptions"/>.
    /// Azure: the real service clients arrive with US-103, so every dependency reports unreachable until then.
    /// Probes are created per request and never connect until checked.
    /// </summary>
    public static IServiceCollection AddReadinessProbes(
        this IServiceCollection services,
        IConfiguration configuration,
        DeploymentEnvironment environment)
    {
        services.AddOptions<DependencyOptions>()
            .Bind(configuration.GetSection(DependencyOptions.SectionName))
            .Validate(options => options.ProbeTimeout > TimeSpan.Zero, "Dependencies:ProbeTimeout must be positive.");

        if (environment == DeploymentEnvironment.Azure)
        {
            foreach (var name in DependencyNames)
            {
                services.AddTransient<IDependencyProbe>(_ => new NotConfiguredProbe(name));
            }

            return services;
        }

        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });

        services.AddTransient<IDependencyProbe>(provider =>
        {
            var options = Options(provider);
            return new TcpPostgresProbe(
                Required(options.Postgres.Host, "Postgres:Host"),
                options.Postgres.Port,
                options.ProbeTimeout);
        });
        services.AddTransient<IDependencyProbe>(provider => Http(provider, "blob", Options(provider).Blob, "Blob:Endpoint"));
        services.AddTransient<IDependencyProbe>(provider =>
            Http(provider, "servicebus", Options(provider).ServiceBus, "ServiceBus:Endpoint"));
        services.AddTransient<IDependencyProbe>(provider => Http(provider, "ollama", Options(provider).Ollama, "Ollama:Endpoint"));

        return services;
    }

    private static DependencyOptions Options(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<DependencyOptions>>().Value;

    private static HttpDependencyProbe Http(
        IServiceProvider provider,
        string name,
        DependencyOptions.HttpEndpoint endpoint,
        string key)
    {
        var uri = endpoint.Endpoint is { IsAbsoluteUri: true } absolute
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                ? absolute
                : throw new InvalidOperationException(
                    $"{DependencyOptions.SectionName}:{key} must be an absolute http or https URI.");
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        return new HttpDependencyProbe(name, client, uri, Options(provider).ProbeTimeout);
    }

    private static string Required(string value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{DependencyOptions.SectionName}:{key} must be set.")
            : value;

    /// <summary>Stands in for a dependency whose Azure client does not exist yet (US-103). Always unreachable.</summary>
    private sealed class NotConfiguredProbe(string name) : IDependencyProbe
    {
        public string Name => name;

        public Task<bool> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
