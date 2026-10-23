using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Sethika.Api.Bootstrap;
using Sethika.Api.Endpoints;
using Sethika.Api.Security;
using Sethika.Application;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure;
using Sethika.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Secrets are never committed: connection strings and the signing key come from User
// Secrets locally (dotnet user-secrets) and from the host's configuration in Azure.
var sqlConnectionString = Required(builder.Configuration.GetConnectionString("Sql"), "ConnectionStrings:Sql");
var redisConnectionString = Required(builder.Configuration.GetConnectionString("Redis"), "ConnectionStrings:Redis");
var jwtSigningKey = Required(builder.Configuration["Jwt:SigningKey"], "Jwt:SigningKey");

// appsettings.json carries these keys with empty values so the shape is discoverable, which
// means a missing secret arrives as "" rather than null. Blank has to fail the same way, or
// the app boots and dies later on a confusing connection error instead of saying what is
// actually wrong.
static string Required(string? value, string key) =>
    string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException(
            $"{key} is not configured. Run: dotnet user-secrets set \"{key}\" \"...\" " +
            "--project src/Sethika.Api")
        : value;

builder.Services.AddSethikaPersistence(sqlConnectionString);
builder.Services.AddSethikaPermissions(redisConnectionString);

// Reads its configuration now, so a production host with the console SMS sender - or with
// one-time codes going to the log - fails to start rather than failing its first signup.
builder.Services.AddSethikaAccess(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSethikaFiles(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSethikaJobs(builder.Configuration, sqlConnectionString);
builder.Services.AddSethikaApplication();

// The three values that stamp the database session context for Row-Level Security are
// read from the validated token, never from the request. Background jobs run as the platform.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSethikaCurrentUser<HttpCurrentUserContext>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = SethikaJwt.Issuer,
            ValidateAudience = true,
            ValidAudience = SethikaJwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SethikaJwt.CreateSigningKey(jwtSigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Policies are built per permission code on demand rather than registered one by one.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpAuthorizationResultHandler>();
builder.Services.AddAuthorization();

// The client address feeds the per-client rate limits and the sign-in history. Behind the Next.js
// rewrite (plan A3) and Azure's front end, the connection comes from a proxy and the client is in
// X-Forwarded-For - but anyone can send that header, so it is believed only from a proxy listed
// here. Loopback is trusted by default, which covers local development. In production the proxy
// addresses must be configured, or every client appears to be the proxy and shares one budget.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

// `dotnet run --project src/Sethika.Api -- bootstrap` creates the first platform administrator
// (plan Q5) and exits instead of serving.
if (args is [BootstrapCommand.Name, ..])
{
    Environment.ExitCode = await BootstrapCommand.RunAsync(app.Services, app.Configuration, Console.Out);
    return;
}

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapSessionEndpoints();
app.MapStepUpEndpoints();
app.MapPasswordEndpoints();
app.MapInstitutionRoleEndpoints();
app.MapOnboardingApplicationEndpoints();
app.MapRegistrationReviewEndpoints();
app.MapLegalEndpoints();
app.MapMeEndpoints();
app.MapInstitutionBookingEndpoints();

app.Run();

// Exposed so Sethika.Api.IntegrationTests can boot the host with WebApplicationFactory.
public partial class Program;
