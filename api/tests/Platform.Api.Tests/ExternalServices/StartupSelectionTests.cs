using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Api.Tests.ExternalServices;

/// <summary>
/// US-103 criterion 3: switching environments needs no code change. The same build starts in either mode from
/// settings only, and in azure mode a missing setting stops startup with a message that names the setting and
/// never shows a value. These tests change the ENVIRONMENT process variable, so they run alone.
///
/// Contract: local mode starts with the defaults in appsettings.json and no extra settings (the US-101/US-102 tests
/// rely on this); azure mode validates these settings at startup (ValidateOnStart or equivalent):
/// Services:Blob:AccountUrl, Services:ServiceBus:Namespace, Services:KeyVault:Uri, ModelGateway:Endpoint,
/// ModelGateway:Model. An empty value counts as missing. Startup makes no network call.
/// </summary>
[Collection(nameof(ProcessEnvironmentCollection))]
public sealed class StartupSelectionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    public static TheoryData<string> RequiredAzureSettings() =>
        new(ExternalServicesHarness.AzureSettings().Keys);

    [Fact]
    public async Task SameBuild_StartsLocal_FromSettingsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var app = factory.WithWebHostBuilder(_ => { });
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertImplementations(app.Services, row => row.Local);
    }

    [Fact]
    public async Task SameBuild_StartsAzure_FromSettingsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Environment.SetEnvironmentVariable(LocalEnvironmentFixture.Variable, "azure");
        try
        {
            using var app = WithSettings(ExternalServicesHarness.AzureSettings());
            using var client = app.CreateClient();
            using var response = await client.GetAsync("/health", cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertImplementations(app.Services, row => row.Azure);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalEnvironmentFixture.Variable, LocalEnvironmentFixture.Local);
        }
    }

    [Theory]
    [MemberData(nameof(RequiredAzureSettings))]
    public void Azure_MissingSetting_StopsStartup_NamingKeyNotValue(string missingKey)
    {
        var settings = ExternalServicesHarness.AzureSettings();
        var otherValues = settings.Where(pair => pair.Key != missingKey).Select(pair => pair.Value!).ToList();
        settings[missingKey] = string.Empty;

        Exception? error;
        Environment.SetEnvironmentVariable(LocalEnvironmentFixture.Variable, "azure");
        try
        {
            using var app = WithSettings(settings);
            error = Record.Exception(() => app.CreateClient().Dispose());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalEnvironmentFixture.Variable, LocalEnvironmentFixture.Local);
        }

        Assert.NotNull(error);
        var messages = AllMessages(error);
        var named = messages.Contains(missingKey, StringComparison.OrdinalIgnoreCase)
            || messages.Contains(missingKey.Replace(":", "__", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.True(named, $"The startup error must name {missingKey}. It was: {messages}");
        foreach (var value in otherValues)
        {
            Assert.DoesNotContain(value, messages, StringComparison.OrdinalIgnoreCase);
        }
    }

    private WebApplicationFactory<Program> WithSettings(IDictionary<string, string?> settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static void AssertImplementations(
        IServiceProvider services,
        Func<(Type Service, string Local, string Azure), string> expected)
    {
        using var scope = services.CreateScope();
        var actual = ExternalServicesHarness.Implementations
            .Select(row => (row.Service.Name, Expected: expected(row), Actual: scope.ServiceProvider.GetService(row.Service)?.GetType().Name))
            .Where(row => row.Expected != row.Actual)
            .Select(row => $"{row.Name}: expected {row.Expected}, got {row.Actual ?? "nothing"}")
            .ToList();
        Assert.Empty(actual);
    }

    private static string AllMessages(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(" | ", messages);
    }
}
