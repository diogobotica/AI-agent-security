using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Platform.Api.Tests;

/// <summary>
/// US-102 and rule 2: ENVIRONMENT chooses the implementations of external services. The API starts only when the
/// ENVIRONMENT process variable is "local" or "azure"; any other value, or none, stops startup with an error that
/// names ENVIRONMENT. See <see cref="LocalEnvironmentFixture"/> for why it is the process variable and not an
/// IConfiguration key.
/// </summary>
[Collection(nameof(ProcessEnvironmentCollection))]
public sealed class EnvironmentSettingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("staging")]
    [InlineData("production")]
    public void Startup_FailsWhenEnvironmentMissingOrUnknown(string? value)
    {
        Exception? error;
        using (ProcessEnvironment(value))
        {
            using var app = factory.WithWebHostBuilder(_ => { });
            error = Record.Exception(() => app.CreateClient().Dispose());
        }

        Assert.NotNull(error);
        Assert.Contains("ENVIRONMENT", AllMessages(error), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("azure")]
    public async Task Startup_SucceedsWhenEnvironmentIsLocalOrAzure(string value)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        HttpStatusCode status;
        using (ProcessEnvironment(value))
        {
            using var app = factory.WithWebHostBuilder(_ => { });
            using var client = app.CreateClient();
            using var response = await client.GetAsync("/health", cancellationToken);
            status = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, status);
    }

    /// <summary>Sets ENVIRONMENT for the duration of the scope (null removes it), then restores "local".</summary>
    private static RestoreLocal ProcessEnvironment(string? value)
    {
        Environment.SetEnvironmentVariable(LocalEnvironmentFixture.Variable, value);
        return new RestoreLocal();
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

    private sealed class RestoreLocal : IDisposable
    {
        public void Dispose() =>
            Environment.SetEnvironmentVariable(LocalEnvironmentFixture.Variable, LocalEnvironmentFixture.Local);
    }
}
