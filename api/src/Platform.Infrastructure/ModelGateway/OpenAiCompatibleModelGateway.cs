using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Options;
using Platform.Core.External;

namespace Platform.Infrastructure.ModelGateway;

/// <summary>
/// The model gateway over the OpenAI-compatible chat completions API: Ollama locally, Azure AI Foundry in Azure
/// (rule 3: every model call goes through here). The model comes from configuration only. Locally no Authorization
/// header is sent; in Azure a managed-identity token for <see cref="ModelGatewayOptions.TokenScope"/> is added.
/// Prompts and replies are never logged, and error messages carry only the HTTP status.
/// Routing, cost recording and ceilings arrive with US-107.
/// </summary>
public sealed class OpenAiCompatibleModelGateway(
    IHttpClientFactory httpClients,
    IOptions<ModelGatewayOptions> options,
    TokenCredential? credential = null) : IModelGateway
{
    /// <summary>The named HttpClient used for model calls.</summary>
    public const string HttpClientName = "model-gateway";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        var settings = options.Value;
        var model = string.IsNullOrWhiteSpace(settings.Model)
            ? throw new InvalidOperationException($"Missing setting {ModelGatewayOptions.Section}:Model.")
            : settings.Model;
        var completions = CompletionsUri(settings.Endpoint);

        var body = new ChatRequest(model, [new ChatMessage("user", request.Prompt)], request.MaxOutputTokens, Stream: false);
        using var message = new HttpRequestMessage(HttpMethod.Post, completions)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        if (credential is not null && !string.IsNullOrWhiteSpace(settings.TokenScope))
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([settings.TokenScope]), cancellationToken).ConfigureAwait(false);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.SendAsync(message, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The model gateway returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var reply = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, timeout.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The model gateway returned an empty reply.");
        var text = reply.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("The model gateway reply has no message.");
        return new ModelResponse(text, reply.Usage?.PromptTokens ?? 0, reply.Usage?.CompletionTokens ?? 0);
    }

    public override string ToString() => nameof(OpenAiCompatibleModelGateway);

    private static Uri CompletionsUri(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint.EndsWith('/') ? endpoint : endpoint + "/", UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Setting {ModelGatewayOptions.Section}:Endpoint must be an absolute URL.");
        }

        return new Uri(baseUri, "chat/completions");
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens,
        bool Stream);

    private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices, ChatUsage? Usage);

    private sealed record ChatChoice(ChatReplyMessage? Message);

    private sealed record ChatReplyMessage(string? Role, string? Content);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
