using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Tests.ExternalServices;
using Platform.Core.External;

namespace Platform.Api.Tests.Integration;

/// <summary>
/// US-103 criteria 4 and 5 against the US-102 local stack (Azurite, the Service Bus emulator, Ollama), reached on the
/// loopback ports that docker-compose.yml publishes. Run with the stack up (python scripts/local_stack.py up):
///
///     dotnet test --filter-trait "Category=Integration"
///
/// Offline runs exclude them: dotnet test --filter-not-trait "Category=Integration".
/// Settings come from the repository's git-ignored .env (AZURITE_ACCOUNT_NAME, AZURITE_ACCOUNT_KEY, OLLAMA_MODEL);
/// the Service Bus emulator uses its documented placeholder key. Only neutral marker content is written.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LocalServicesTests
{
    private const string QueueName = "run-jobs";
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(10);
    private static readonly byte[] EvidenceBytes = Encoding.UTF8.GetBytes("{\"marker\":\"neutral evidence\"}");

    [Fact]
    public async Task EvidenceStore_WriteThenRead_OnAzurite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = LocalStack.Services();
        var store = provider.GetRequiredService<IEvidenceStore>();
        var tenant = Guid.NewGuid();
        var run = Guid.NewGuid();

        using var upload = new MemoryStream(EvidenceBytes);
        var reference = await store.PutAsync(tenant, run, "marker-evidence.json", upload, "application/json", cancellationToken);
        await using var download = await store.GetAsync(tenant, reference, cancellationToken);
        using var copy = new MemoryStream();
        await download.CopyToAsync(copy, cancellationToken);

        Assert.Equal(tenant, reference.TenantId);
        Assert.StartsWith($"{tenant}/{run}/", reference.Path, StringComparison.Ordinal);
        Assert.Equal(EvidenceBytes, copy.ToArray());
    }

    [Fact]
    public async Task EvidenceStore_Get_WithOtherTenant_Fails_OnAzurite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = LocalStack.Services();
        var store = provider.GetRequiredService<IEvidenceStore>();

        using var upload = new MemoryStream(EvidenceBytes);
        var reference = await store.PutAsync(Guid.NewGuid(), Guid.NewGuid(), "marker-evidence.json", upload, "application/json", cancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetAsync(Guid.NewGuid(), reference, cancellationToken));
    }

    [Fact]
    public async Task Queue_SendThenReceive_OnEmulator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = LocalStack.Services();
        var queue = provider.GetRequiredService<IQueue>();
        await Drain(queue, cancellationToken);
        var sent = new QueueMessage("marker-" + Guid.NewGuid().ToString("N"), Encoding.UTF8.GetBytes("{\"marker\":\"neutral message\"}"));

        await queue.SendAsync(QueueName, sent, cancellationToken);
        var received = await queue.ReceiveAsync(QueueName, ReceiveWait, cancellationToken);

        Assert.NotNull(received);
        Assert.Equal(sent.MessageId, received.MessageId);
        Assert.Equal(sent.Body, received.Body);
    }

    [Fact]
    public async Task Queue_SameMessageIdSentTwice_IsDeliveredOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = LocalStack.Services();
        var queue = provider.GetRequiredService<IQueue>();
        await Drain(queue, cancellationToken);
        var messageId = "marker-" + Guid.NewGuid().ToString("N");
        var body = Encoding.UTF8.GetBytes("{\"marker\":\"neutral duplicate\"}");

        await queue.SendAsync(QueueName, new QueueMessage(messageId, body), cancellationToken);
        await queue.SendAsync(QueueName, new QueueMessage(messageId, body), cancellationToken);
        var first = await queue.ReceiveAsync(QueueName, ReceiveWait, cancellationToken);
        var second = await queue.ReceiveAsync(QueueName, TimeSpan.FromSeconds(3), cancellationToken);

        Assert.NotNull(first);
        Assert.Equal(messageId, first.MessageId);
        Assert.Null(second);
    }

    [Fact]
    public async Task ModelGateway_ReturnsCompletion_FromOllama()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = LocalStack.Services();
        var gateway = provider.GetRequiredService<IModelGateway>();

        var response = await gateway.CompleteAsync(
            new ModelRequest("Reply with the single word: ready.", MaxOutputTokens: 16),
            cancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.True(response.InputTokens > 0, "Ollama reports prompt tokens.");
        Assert.True(response.OutputTokens > 0, "Ollama reports completion tokens.");
    }

    /// <summary>Removes messages left over from earlier runs so each test sees only its own.</summary>
    private static async Task Drain(IQueue queue, CancellationToken cancellationToken)
    {
        while (await queue.ReceiveAsync(QueueName, TimeSpan.FromSeconds(1), cancellationToken) is not null)
        {
        }
    }
}

/// <summary>Builds local-mode services that reach the US-102 stack on its loopback ports.</summary>
internal static class LocalStack
{
    public static Microsoft.Extensions.DependencyInjection.ServiceProvider Services()
    {
        var env = ReadDotEnv();
        var account = Required(env, "AZURITE_ACCOUNT_NAME");
        var settings = new Dictionary<string, string?>
        {
            ["Services:Blob:ConnectionStringSecret"] = ExternalServicesHarness.BlobSecretName,
            ["Services:ServiceBus:ConnectionStringSecret"] = ExternalServicesHarness.ServiceBusSecretName,
            ["Secrets:" + ExternalServicesHarness.BlobSecretName] = ExternalServicesHarness.BlobConnection(
                $"http://127.0.0.1:10000/{account}", account, Required(env, "AZURITE_ACCOUNT_KEY")),
            // The emulator's documented placeholder key (not a secret).
            ["Secrets:" + ExternalServicesHarness.ServiceBusSecretName] = ExternalServicesHarness.ServiceBusConnection(
                "127.0.0.1", "RootManageSharedAccessKey", "SAS_KEY_VALUE"),
            ["ModelGateway:Endpoint"] = "http://127.0.0.1:11434/v1/",
            ["ModelGateway:Model"] = Required(env, "OLLAMA_MODEL"),
        };
        return ExternalServicesHarness.Build(DeploymentEnvironment.Local, settings);
    }

    private static string Required(IReadOnlyDictionary<string, string> env, string name) =>
        env.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{name} is missing from .env: run python scripts/local_stack.py up first.");

    private static Dictionary<string, string> ReadDotEnv()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        var path = directory is null ? null : Path.Combine(directory.FullName, ".env");
        if (path is null || !File.Exists(path))
        {
            throw new InvalidOperationException("The repository .env is missing: run python scripts/local_stack.py up first.");
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('=', StringComparison.Ordinal))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"', '\''), StringComparer.Ordinal);
    }
}
