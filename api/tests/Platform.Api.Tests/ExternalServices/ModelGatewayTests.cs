using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Core.External;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 4 (offline part) and rule 3: the model gateway speaks the OpenAI-compatible chat completions API
/// (Ollama locally, Azure AI Foundry in Azure) over an HttpClient from IHttpClientFactory. The model name comes only
/// from configuration (ModelGateway:Model). Requests go to ModelGateway:Endpoint + "chat/completions"; the reply
/// gives the text and the input and output token counts. An in-memory handler answers; no network, no real model.
/// </summary>
public sealed class ModelGatewayTests
{
    private const string CompletionsUri = ExternalServicesHarness.LocalModelEndpoint + "chat/completions";
    private const string NeutralPrompt = "neutral marker prompt";

    private static readonly string CompletionReply = """
        {
          "id": "marker-completion",
          "object": "chat.completion",
          "model": "marker-model",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "marker completion text" }, "finish_reason": "stop" }
          ],
          "usage": { "prompt_tokens": 7, "completion_tokens": 3, "total_tokens": 10 }
        }
        """;

    [Fact]
    public async Task ModelGateway_ParsesTextAndTokenCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, CompletionReply));
        using var provider = LocalGateway(handler);
        var gateway = provider.GetRequiredService<IModelGateway>();

        var response = await gateway.CompleteAsync(new ModelRequest(NeutralPrompt), cancellationToken);

        Assert.Equal("marker completion text", response.Text);
        Assert.Equal(7, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(CompletionsUri, request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ModelGateway_TakesModelNameFromConfiguration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, CompletionReply));
        using var provider = LocalGateway(handler);
        var gateway = provider.GetRequiredService<IModelGateway>();

        await gateway.CompleteAsync(new ModelRequest(NeutralPrompt), cancellationToken);

        var (_, body) = Assert.Single(handler.Requests);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(ExternalServicesHarness.MarkerModel, json.RootElement.GetProperty("model").GetString());
        var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Contains(messages, message =>
            message.GetProperty("role").GetString() == "user"
            && message.GetProperty("content").GetString() == NeutralPrompt);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ModelGateway_FailsOnErrorStatus(HttpStatusCode status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(status, """{"error":{"message":"marker failure"}}"""));
        using var provider = LocalGateway(handler);
        var gateway = provider.GetRequiredService<IModelGateway>();

        await Assert.ThrowsAnyAsync<Exception>(() => gateway.CompleteAsync(new ModelRequest(NeutralPrompt), cancellationToken));
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceProvider LocalGateway(StubHttpHandler handler) =>
        ExternalServicesHarness.Build(
            DeploymentEnvironment.Local,
            ExternalServicesHarness.LocalSettings(),
            services => services.UseHttpHandler(handler));
}
