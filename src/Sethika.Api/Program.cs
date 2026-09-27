using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddSethikaApplication();

// The three values that stamp the database session context for Row-Level Security are
// read from the validated token, never from the request.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

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

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapSessionEndpoints();
app.MapLegalEndpoints();

// Returns the caller's own effective permission codes. The Next.js app reads this once per
// session to decide what to render; it is a convenience for the UI, not a security boundary -
// each endpoint still carries its own [HasPermission].
app.MapGet("/api/me/permissions", async (
        ICurrentUserContext currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken) =>
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var effective = await permissions
            .GetEffectivePermissionsAsync(userId, currentUser.InstitutionId, cancellationToken);

        return Results.Ok(new
        {
            userId,
            institutionId = currentUser.InstitutionId,
            isPlatform = currentUser.IsPlatform,
            permissions = effective.Codes.OrderBy(c => c, StringComparer.Ordinal),
        });
    })
    .RequireAuthorization();

// Demonstrates the guard, and is what the integration tests exercise.
app.MapGet("/api/bookings/calendar", () => Results.Ok(new { ok = true }))
   .WithMetadata(new HasPermissionAttribute("BOOKINGS.CALENDAR.VIEW"));

app.MapPost("/api/bookings/{bookingNo}/approve", (string bookingNo) => Results.Ok(new { bookingNo }))
   .WithMetadata(new HasPermissionAttribute("BOOKINGS.CALENDAR.APPROVE"));

app.Run();

// Exposed so Sethika.Api.IntegrationTests can boot the host with WebApplicationFactory.
public partial class Program;
