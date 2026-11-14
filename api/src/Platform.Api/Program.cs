var builder = WebApplication.CreateBuilder(args);

// Do not reveal the server software in responses.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();

/// <summary>Entry point; public partial so WebApplicationFactory can host the API in tests.</summary>
public partial class Program
{
}
