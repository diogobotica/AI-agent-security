using Platform.Api;
using Platform.Api.ExternalServices;
using Platform.Api.Health;

var builder = WebApplication.CreateBuilder(args);

// ENVIRONMENT (local or azure) chooses the external-service implementations; startup stops on any other value.
var environment = EnvironmentSetting.Read();

// Do not reveal the server software in responses.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllers();
builder.Services.AddReadinessProbes(builder.Configuration, environment);
builder.Services.AddExternalServices(environment, builder.Configuration);

var app = builder.Build();

app.MapControllers();

app.Run();

/// <summary>Entry point; public partial so WebApplicationFactory can host the API in tests.</summary>
public partial class Program
{
}
