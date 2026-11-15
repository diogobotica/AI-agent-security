namespace Platform.Infrastructure.ModelGateway;

/// <summary>
/// Where the model gateway sends requests. The model name lives only here, in configuration (rule 3): never in code.
/// </summary>
public sealed class ModelGatewayOptions
{
    public const string Section = "ModelGateway";

    /// <summary>The OpenAI-compatible base URL, ending in <c>/v1/</c> (Ollama locally, Azure AI Foundry in Azure).</summary>
    public string? Endpoint { get; set; }

    /// <summary>The model to use.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// The token scope for managed identity in Azure (Azure AI Foundry). Empty means no Authorization header (Ollama).
    /// </summary>
    public string? TokenScope { get; set; }

    /// <summary>How long one model call may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}
