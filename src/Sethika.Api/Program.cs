var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so Sethika.Api.IntegrationTests can reach the entry point later
// (WebApplicationFactory<Program> needs a non-internal marker).
public partial class Program;
