namespace Platform.Api.Health;

/// <summary>
/// Checks an HTTP dependency (Azurite, the Service Bus emulator, Ollama): sends GET to a fixed endpoint and succeeds on
/// any HTTP response, whatever its status code. Only the configured endpoint is contacted.
/// </summary>
public sealed class HttpDependencyProbe(string name, HttpClient client, Uri endpoint, TimeSpan timeout) : IDependencyProbe
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
