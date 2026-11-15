namespace Platform.Api.Health;

/// <summary>
/// Where the readiness probes look for each dependency (configuration section <c>Dependencies</c>). Holds hosts, ports
/// and endpoints only, never credentials. Override per environment, for example
/// <c>Dependencies__Postgres__Host</c>.
/// </summary>
public sealed class DependencyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Dependencies";

    /// <summary>How long a single probe waits before it reports the dependency unreachable.</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>PostgreSQL host and port (TCP handshake only).</summary>
    public PostgresEndpoint Postgres { get; set; } = new();

    /// <summary>Blob storage (Azurite locally).</summary>
    public HttpEndpoint Blob { get; set; } = new();

    /// <summary>Service Bus (the emulator's health endpoint locally).</summary>
    public HttpEndpoint ServiceBus { get; set; } = new();

    /// <summary>Ollama model server (<c>GET /api/tags</c>; lists models, never runs one).</summary>
    public HttpEndpoint Ollama { get; set; } = new();

    /// <summary>A PostgreSQL server address.</summary>
    public sealed class PostgresEndpoint
    {
        /// <summary>Host name or IP address.</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>TCP port.</summary>
        public int Port { get; set; } = 5432;
    }

    /// <summary>An HTTP endpoint that the probe sends GET to.</summary>
    public sealed class HttpEndpoint
    {
        /// <summary>Absolute http or https URI.</summary>
        public Uri? Endpoint { get; set; }
    }
}
