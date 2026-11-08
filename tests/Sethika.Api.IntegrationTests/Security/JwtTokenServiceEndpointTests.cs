using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Sends tokens from <see cref="JwtTokenService"/> to the real API pipeline, against the real
/// database and Redis.
/// </summary>
/// <remarks>
/// The issuing side and the validating side are configured in different places - the service
/// here, the bearer options in <c>Program.cs</c> - and each can be internally consistent while
/// disagreeing with the other on issuer, audience, key encoding or a claim name. The result is a
/// login that succeeds and every call after it failing with 401, or worse, a claim silently not
/// read and a session running without its tenant. Only a round trip through the host shows the
/// two sides agree.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class JwtTokenServiceEndpointTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "jwt-endpoint-test-signing-key-at-least-32-bytes!!";

    private readonly ConcurrentQueue<SessionSnapshot> _sessions = new();
    private readonly List<Guid> _donors = [];

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
                services.AddScoped<IDbConnectionFactory>(provider => new RecordingConnectionFactory(
                    new SqlConnectionFactory(database.ConnectionString, provider.GetRequiredService<ICurrentUserContext>()),
                    _sessions)));
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();

        if (_donors.Count > 0)
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.ExecuteAsync(
                """
                DELETE FROM auth.UserRoles WHERE UserId IN @donors;
                DELETE FROM auth.Users     WHERE Id     IN @donors;
                """,
                new { donors = _donors });
        }
    }

    /// <summary>What the database session of one request was stamped with, and what RLS let it see.</summary>
    private sealed record SessionSnapshot(Guid? UserId, int? InstitutionId, bool? IsPlatform, int VisibleBookings);

    /// <summary>
    /// The application's own Dapper connection factory, plus one read of the session context on every
    /// connection it opens.
    /// </summary>
    /// <remarks>
    /// The read runs on the connection the request then uses, after the real stamp, so it reports what
    /// RLS will actually apply - not what the token said or what the response chose to echo.
    /// <c>booking.Bookings</c> is tenant-filtered and the seed puts rows in two institutions, so a
    /// session with no institution sees none of them unless it is the platform.
    /// </remarks>
    private sealed class RecordingConnectionFactory(IDbConnectionFactory inner, ConcurrentQueue<SessionSnapshot> seen)
        : IDbConnectionFactory
    {
        private const string SnapshotSql =
            """
            SELECT CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER) AS UserId,
                   CAST(SESSION_CONTEXT(N'InstitutionId') AS INT)       AS InstitutionId,
                   CAST(SESSION_CONTEXT(N'IsPlatform') AS BIT)          AS IsPlatform,
                   (SELECT COUNT(*) FROM booking.Bookings)              AS VisibleBookings;
            """;

        public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = await inner.OpenConnectionAsync(cancellationToken);
            seen.Enqueue(await connection.QuerySingleAsync<SessionSnapshot>(
                new CommandDefinition(SnapshotSql, cancellationToken: cancellationToken)));
            return connection;
        }
    }

    /// <summary>An ordinary donor: a Donor account holding only the DONOR system role, as signup leaves it.</summary>
    private async Task<Guid> CreateDonorAsync()
    {
        var donor = Guid.CreateVersion7();
        _donors.Add(donor);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status)
            VALUES (@donor, 'Donor', N'Forgery Test Donor', @phone, 1, 'en', 'Active');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @donor, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { donor, phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture) });

        return donor;
    }

    private static JwtTokenService Service(TimeProvider? time = null, string signingKey = SigningKey) =>
        JwtTokenServiceTests.CreateService(signingKey, time ?? TimeProvider.System);

    private async Task<HttpResponseMessage> GetAsync(string path, IssuedTokens tokens)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.Access.Token);

        return await client.GetAsync(path);
    }

    [Fact]
    public async Task TheApiAcceptsAnIssuedTokenAndReadsBackTheSameSessionContext()
    {
        var tokens = Service().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/me/permissions", tokens);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var me = body.RootElement;

        Assert.Equal(SethikaTestDatabase.AlphaUserId, me.GetProperty("userId").GetGuid());
        Assert.Equal(database.AlphaInstitutionId, me.GetProperty("institutionId").GetInt32());
        Assert.False(me.GetProperty("isPlatform").GetBoolean());

        // The permissions come back from the view under RLS, so the institution in the token
        // reached the database session context, not just the response.
        Assert.Contains(
            "BOOKINGS.CALENDAR.VIEW",
            me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task TheApiReadsThePlatformFlagAndNoInstitution()
    {
        var tokens = Service().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, InstitutionId: null, IsPlatform: true));

        _sessions.Clear();
        using var response = await GetAsync("/api/me/permissions", tokens);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(body.RootElement.GetProperty("isPlatform").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("institutionId").ValueKind);

        // The flag reached the database session, and RLS let it see both tenants' bookings. This is
        // also the control for the forgery tests below: it shows the recorder does see a platform
        // session when there is one, so "not the platform" there is a finding and not a blind spot.
        Assert.NotEmpty(_sessions);
        Assert.All(_sessions, session =>
        {
            Assert.True(session.IsPlatform);
            Assert.True(session.VisibleBookings > 0);
        });
    }

    [Fact]
    public async Task AnIssuedTokenPassesAPermissionGuardedEndpoint()
    {
        var tokens = Service().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/institution/bookings?from=2026-01-01&to=2026-01-31", tokens);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKeyIsRefused()
    {
        var tokens = Service(signingKey: "a-different-key-also-long-enough-to-be-valid!!").Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/institution/bookings?from=2026-01-01&to=2026-01-31", tokens);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    // Fourteen minutes old is still inside its fifteen. Without this control, the sixteen-minute
    // case could be passing because the token is refused for some other reason entirely.
    [InlineData(14, HttpStatusCode.OK)]
    // Sixteen minutes old is a minute past expiry, well beyond the API's thirty-second clock skew.
    [InlineData(16, HttpStatusCode.Unauthorized)]
    public async Task AnAccessTokenIsAcceptedForFifteenMinutesAndNoLonger(
        int minutesAgo,
        HttpStatusCode expected)
    {
        var issuedAt = new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

        var tokens = Service(issuedAt).Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/institution/bookings?from=2026-01-01&to=2026-01-31", tokens);

        Assert.Equal(expected, response.StatusCode);
    }

    // ------------------------------------------------------------------ the platform flag cannot be forged

    /// <summary>
    /// Names a client might try, in the hope that a proxy, a later refactor or a convenience reads the
    /// flag from the request: the claim's own name and the spellings a developer would reach for.
    /// </summary>
    /// <remarks>
    /// One name per request. Sent together, two spellings that differ only in case - query keys and
    /// header names are case-insensitive - arrive as one key with two values, and a mutant that read
    /// <c>isPlatform</c> from the query string survived exactly that way.
    /// </remarks>
    private static readonly string[] ForgedNames =
    [
        SethikaClaimTypes.IsPlatform,
        "sethika-platform",
        "X-Sethika-Platform",
        "isPlatform",
        "IsPlatform",
        "is_platform",
        "platform",
        "X-Platform",
    ];

    /// <summary>The names <paramref name="channel"/> can carry: no header name may contain a colon.</summary>
    private static IEnumerable<string> ForgedNamesFor(string channel) =>
        channel == "header" ? ForgedNames.Where(n => !n.Contains(':', StringComparison.Ordinal)) : ForgedNames;

    private sealed record Observed(HttpStatusCode Status, string Body, SessionSnapshot[] Sessions);

    /// <summary>
    /// One request with the donor's real token, and - unless <paramref name="channel"/> is null -
    /// <paramref name="name"/> set to true in the header, query string or JSON body.
    /// </summary>
    private async Task<Observed> SendAsDonorAsync(string path, IssuedTokens tokens, string? channel = null, string? name = null)
    {
        var uri = channel == "query" ? $"{path}{(path.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{Uri.EscapeDataString(name!)}=true" : path;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);

        if (channel == "header")
        {
            request.Headers.Add(name!, "true");
        }
        else if (channel == "body")
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, bool> { [name!] = true }),
                Encoding.UTF8,
                "application/json");
        }

        _sessions.Clear();
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(request);

        return new Observed(response.StatusCode, await response.Content.ReadAsStringAsync(), [.. _sessions]);
    }

    [Theory]
    // Authenticated-only: answers with isPlatform and the permissions, read from the view under RLS.
    [InlineData("header", "/api/me/permissions")]
    [InlineData("query", "/api/me/permissions")]
    [InlineData("body", "/api/me/permissions")]
    // Permission-guarded. No platform-scoped endpoint exists yet - the catalogue has only the
    // institution portal - so this stands in for one: its authorization reads the caller's
    // permissions over a stamped connection, which is where a believed flag would lift RLS.
    [InlineData("header", "/api/institution/bookings?from=2026-01-01&to=2026-01-31")]
    [InlineData("query", "/api/institution/bookings?from=2026-01-01&to=2026-01-31")]
    [InlineData("body", "/api/institution/bookings?from=2026-01-01&to=2026-01-31")]
    public async Task ADonorCannotForgeThePlatformFlag(string channel, string path)
    {
        // Plan 5.8: sethika:platform is read from the validated token and nowhere else. Every request
        // carries the donor's real token, so the forgery is the only difference from the honest one.
        var donor = await CreateDonorAsync();
        var tokens = Service().Issue(new TokenSubject(donor, InstitutionId: null, IsPlatform: false));

        var honest = await SendAsDonorAsync(path, tokens);

        // The donor's session: no institution, not the platform, and RLS hiding every tenant's bookings.
        var donorSession = new SessionSnapshot(donor, InstitutionId: null, IsPlatform: false, VisibleBookings: 0);
        Assert.NotEmpty(honest.Sessions);
        Assert.All(honest.Sessions, session => Assert.Equal(donorSession, session));
        Assert.Equal(path == "/api/me/permissions" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, honest.Status);

        foreach (var name in ForgedNamesFor(channel))
        {
            var forged = await SendAsDonorAsync(path, tokens, channel, name);

            // The same answer, byte for byte - the isPlatform field and the permission list included -
            // and the same database session.
            Assert.True(honest.Status == forged.Status, $"{channel} '{name}' changed the status to {forged.Status}");
            Assert.True(honest.Body == forged.Body, $"{channel} '{name}' changed the body to {forged.Body}");
            Assert.True(
                honest.Sessions.SequenceEqual(forged.Sessions),
                $"{channel} '{name}' changed the database session to {string.Join(", ", forged.Sessions)}");
        }
    }

    [Theory]
    // The control: the same claim under the API's own key is believed. Without it the refusals below
    // could be passing because platform tokens are refused for some other reason.
    [InlineData("signed", HttpStatusCode.OK)]
    [InlineData("other-key", HttpStatusCode.Unauthorized)]
    // A donor's real token with its payload swapped for one that says platform, the signature kept.
    [InlineData("tampered", HttpStatusCode.Unauthorized)]
    // No signature at all.
    [InlineData("alg-none", HttpStatusCode.Unauthorized)]
    // The API's own key under another HMAC: correctly signed, but not the algorithm the API issues.
    // HS384 because the library takes it from any key of 48 bytes or more (this one has 49).
    [InlineData("hs384", HttpStatusCode.Unauthorized)]
    public async Task APlatformClaimIsBelievedOnlyUnderTheApisSignature(string token, HttpStatusCode expected)
    {
        var donor = await CreateDonorAsync();
        var platform = new TokenSubject(donor, InstitutionId: null, IsPlatform: true);

        var access = token switch
        {
            "signed" => Service().Issue(platform).Access.Token,
            "other-key" => Service(signingKey: "a-different-key-also-long-enough-to-be-valid!!").Issue(platform).Access.Token,
            "tampered" => WithPayloadOf(
                Service().Issue(platform with { IsPlatform = false }).Access.Token,
                Service().Issue(platform).Access.Token),
            "alg-none" => Unsigned(Service().Issue(platform).Access.Token),
            "hs384" => ResignedWithHs384(Service().Issue(platform).Access.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };

        _sessions.Clear();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var response = await client.GetAsync("/api/me/permissions");

        Assert.Equal(expected, response.StatusCode);

        if (expected == HttpStatusCode.OK)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("isPlatform").GetBoolean());
            Assert.NotEmpty(_sessions);
            Assert.All(_sessions, session => Assert.True(session.IsPlatform));
        }
        else
        {
            // Refused at authentication: no database session was opened for it at all.
            Assert.Empty(_sessions);
        }
    }

    /// <summary><paramref name="signed"/>'s header and signature around <paramref name="payloadFrom"/>'s claims.</summary>
    private static string WithPayloadOf(string signed, string payloadFrom)
    {
        var parts = signed.Split('.');
        var payload = payloadFrom.Split('.')[1];
        Assert.NotEqual(parts[1], payload);
        return $"{parts[0]}.{payload}.{parts[2]}";
    }

    /// <summary>The same header and claims, but <c>"alg": "HS384"</c> and signed so with the API's own key.</summary>
    private static string ResignedWithHs384(string signed)
    {
        var original = new JsonWebToken(signed);
        Assert.Equal(SecurityAlgorithms.HmacSha256, original.Alg);

        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(signed.Split('.')[1]));
        var forged = new JsonWebTokenHandler().CreateToken(
            payload,
            new SigningCredentials(SethikaJwt.CreateSigningKey(SigningKey), SecurityAlgorithms.HmacSha384));
        Assert.Equal(SecurityAlgorithms.HmacSha384, new JsonWebToken(forged).Alg);

        // The control for this case: a validator that does take HS384 believes the forgery, so the API's
        // refusal is about the algorithm and not a malformed token.
        var check = new JsonWebTokenHandler().ValidateTokenAsync(forged, new TokenValidationParameters
        {
            ValidIssuer = SethikaJwt.Issuer,
            ValidAudience = SethikaJwt.Audience,
            IssuerSigningKey = SethikaJwt.CreateSigningKey(SigningKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha384],
        }).GetAwaiter().GetResult();
        Assert.True(check.IsValid, check.Exception?.Message);

        return forged;
    }

    /// <summary>The same claims under <c>"alg": "none"</c>, with the signature removed.</summary>
    private static string Unsigned(string signed) =>
        $"{Base64Url.EncodeToString("""{"alg":"none","typ":"JWT"}"""u8)}.{signed.Split('.')[1]}.";
}
