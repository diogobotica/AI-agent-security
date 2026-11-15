using System.Net;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Core.External;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 6 and rule 4: secret values never appear in logs, exception messages or the ToString output of
/// the secret stores. The local store reads in-memory settings; the Key Vault store talks to an in-memory fake vault
/// through the IHttpClientFactory handler and gets its token from a fake credential (no network, no identity service).
/// Missing secrets throw KeyNotFoundException in both stores.
/// </summary>
public sealed class SecretMaskingTests
{
    private const string StoredName = "markermasked";
    private const string MissingName = "markermissing";

    [Fact]
    public async Task SecretValue_NotInLogsExceptionsOrToString_LocalStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stored = "marker-" + Guid.NewGuid().ToString("N");
        var written = "marker-" + Guid.NewGuid().ToString("N");
        var settings = ExternalServicesHarness.LocalSettings();
        settings["Secrets:" + StoredName] = stored;
        using var logs = new CapturingLoggerProvider();
        using var provider = ExternalServicesHarness.Build(DeploymentEnvironment.Local, settings, logs: logs);
        var store = provider.GetRequiredService<ISecretStore>();

        Assert.Equal(stored, await store.GetSecretAsync(StoredName, cancellationToken));
        await store.SetSecretAsync("markerwritten", written, cancellationToken);
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => store.GetSecretAsync(MissingName, cancellationToken));

        AssertNoValueShown([stored, written], logs, missing, store);
    }

    [Fact]
    public async Task SecretValue_NotInLogsExceptionsOrToString_KeyVaultStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stored = "marker-" + Guid.NewGuid().ToString("N");
        var vault = new StubHttpHandler((request, _) => FakeVault(request, stored));
        var credential = new MarkerCredential();
        using var logs = new CapturingLoggerProvider();
        using var provider = ExternalServicesHarness.Build(
            DeploymentEnvironment.Azure,
            ExternalServicesHarness.AzureSettings(),
            services =>
            {
                services.RemoveAll<TokenCredential>();
                services.AddSingleton<TokenCredential>(credential);
                services.UseHttpHandler(vault);
            },
            logs);
        var store = provider.GetRequiredService<ISecretStore>();

        var read = await store.GetSecretAsync(StoredName, cancellationToken);
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => store.GetSecretAsync(MissingName, cancellationToken));

        Assert.Equal(stored, read);
        Assert.Contains(MissingName, missing.Message, StringComparison.Ordinal);
        Assert.NotEmpty(vault.Requests);
        Assert.True(credential.Requests > 0, "The Key Vault store must take its token from the TokenCredential registered in DI.");
        Assert.All(vault.Requests, entry => Assert.StartsWith(ExternalServicesHarness.VaultUri, entry.Request.RequestUri!.ToString(), StringComparison.Ordinal));
        AssertNoValueShown([stored], logs, missing, store);
    }

    /// <summary>Answers like Key Vault: a bearer challenge without a token, then the secret or a 404.</summary>
    private static HttpResponseMessage FakeVault(HttpRequestMessage request, string stored)
    {
        if (request.Headers.Authorization is null)
        {
            return StubHttpHandler.Json(
                HttpStatusCode.Unauthorized,
                """{"error":{"code":"Unauthorized","message":"marker challenge"}}""",
                ("WWW-Authenticate", "Bearer authorization=\"https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000\", resource=\"https://vault.azure.net\""));
        }

        var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/secrets/" + StoredName, StringComparison.Ordinal))
        {
            var body = "{\"value\":\"" + stored + "\",\"id\":\"" + ExternalServicesHarness.VaultUri + "secrets/" + StoredName
                + "/0123456789abcdef\",\"attributes\":{\"enabled\":true,\"created\":1767258000,\"updated\":1767258000}}";
            return StubHttpHandler.Json(HttpStatusCode.OK, body);
        }

        return StubHttpHandler.Json(
            HttpStatusCode.NotFound,
            """{"error":{"code":"SecretNotFound","message":"A secret with (name/id) markermissing was not found in this key vault."}}""");
    }

    private static void AssertNoValueShown(
        IReadOnlyList<string> values,
        CapturingLoggerProvider logs,
        Exception error,
        ISecretStore store)
    {
        var shown = new List<string>(logs.Messages) { error.ToString(), store.ToString() ?? string.Empty };
        foreach (var value in values)
        {
            Assert.DoesNotContain(shown, text => text.Contains(value, StringComparison.Ordinal));
        }
    }
}
