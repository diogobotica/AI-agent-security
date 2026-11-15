namespace Platform.Core.External;

/// <summary>A request to the model gateway. The model itself comes from configuration, never from callers.</summary>
public sealed record ModelRequest(string Prompt, int? MaxOutputTokens = null);

/// <summary>The model's text and the token counts used for cost tracking.</summary>
public sealed record ModelResponse(string Text, int InputTokens, int OutputTokens);

/// <summary>The only way to call a model (rule 3). Routing, costs and ceilings arrive in US-107.</summary>
public interface IModelGateway
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}
