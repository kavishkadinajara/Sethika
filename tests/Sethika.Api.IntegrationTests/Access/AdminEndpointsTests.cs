using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Plan 04, U4 over HTTP against real SQL Server and Redis: a platform administrator works inside
/// any institution as support (Y1-Y3), only with a ticket issued behind a step-up (Y1a), only while
/// they hold the permission; and the platform's own account administration (Y5-Y6).
/// </summary>
/// <remarks>
/// Alpha gets the ACCESS and INSTITUTION modules switched on and an owner role, as the role tests
/// do. Step-ups are written straight into Redis: the step-up flow has its own tests.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class AdminEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "admin-endpoint-test-signing-key-at-least-32-bytes!!!";
    private const string Password = "Dana!2026Sethika";

    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);

        await ExecuteAsync(
            """
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT @alpha, m.Id, 1, 'Manual', @changedBy FROM auth.Modules m
            WHERE (m.Code = 'ACCESS' OR m.Code = 'INSTITUTION')
              AND NOT EXISTS (SELECT 1 FROM org.InstitutionModules im WHERE im.InstitutionId = @alpha AND im.ModuleId = m.Id);

            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            SELECT @alpha, 'Institution', l.Id, 'A_OWNER', N'Owner', 0 FROM auth.RoleLevels l WHERE l.Code = 'OWNER';
            INSERT auth.RolePermissions (RoleId, PermissionId)
            SELECT r.Id, p.Id FROM auth.Roles r CROSS JOIN auth.Permissions p
            WHERE r.InstitutionId = @alpha AND r.Code = 'A_OWNER' AND p.Code IN ('ACCESS.ROLES.VIEW', 'ACCESS.ROLES.EDIT', 'INSTITUTION.PROFILE.VIEW');
            """,
            new { alpha = database.AlphaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new LoopbackConnectionStartupFilter()));
        });
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(
            """
            DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
            INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;

            -- First: support's edits to the owner role point at the administrator (GrantedBy).
            DELETE FROM auth.RolePermissions WHERE RoleId IN (SELECT Id FROM auth.Roles WHERE Code = 'A_OWNER');
            DELETE FROM auth.UserRoles       WHERE RoleId IN (SELECT Id FROM auth.Roles WHERE Code = 'A_OWNER');
            DELETE FROM auth.Roles           WHERE Code = 'A_OWNER';


            UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.RefreshTokens           WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.LoginHistory            WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.UserPermissionOverrides WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.UserRoles               WHERE UserId IN (SELECT Id FROM @ids) OR AssignedBy IN (SELECT Id FROM @ids);
            DELETE FROM auth.DonorProfiles           WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.Users                   WHERE Id IN (SELECT Id FROM @ids);
            DELETE FROM core.OutboxMessages  WHERE EventType IN ('PlatformAdministration', 'RolePermissionsChanged');
            """,
            new { phones = _phones });

        await _redis.DisposeAsync();
        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class LoopbackConnectionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>A signed-in session: the cookies a browser holds, and the access token.</summary>
    private sealed record Session(Guid UserId, string Phone, string Refresh, string Csrf, string AccessToken, JsonElement Body)
    {
        public string TokenId => new JsonWebToken(AccessToken).Id;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static string NewPhone() => "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>An account, as a platform role holder (<paramref name="platformRole"/>), an Alpha role holder, or neither.</summary>
    private async Task<string> AccountAsync(string? platformRole = null, string? alphaRole = null)
    {
        var phone = NewPhone();
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await ExecuteAsync(
            """
            DECLARE @id UNIQUEIDENTIFIER = NEWID();
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status, MfaRequired)
            VALUES (@id, CASE WHEN @platformRole IS NULL THEN 'Donor' ELSE 'PlatformStaff' END, N'Admin Test', @phone, 1, @hash, 'en', 'Active',
                    CASE WHEN @platformRole IS NULL THEN 0 ELSE 1 END);

            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.InstitutionId IS NULL AND r.Code = @platformRole;
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = @alphaRole;
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.InstitutionId IS NULL AND r.Code = 'DONOR';
            """,
            new { phone, hash, platformRole, alphaRole, alpha = database.AlphaInstitutionId });

        return phone;
    }

    private HttpClient NewClient()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Its own client address: every login otherwise spends one shared loopback budget.
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        return client;
    }

    private static Session Parse(Guid userId, string phone, HttpResponseMessage response, JsonElement body)
    {
        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);
        return new Session(
            userId,
            phone,
            cookies.Single(c => c.Name == "sethika-refresh").Value.ToString(),
            cookies.Single(c => c.Name == "sethika-csrf").Value.ToString(),
            body.GetProperty("accessToken").GetString()!,
            body);
    }

    private async Task<Session> LogInAsync(string phone)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var userId = Guid.Parse(new JsonWebToken(body.GetProperty("accessToken").GetString()!).GetClaim(SethikaClaimTypes.UserId).Value);
        return Parse(userId, phone, response, body);
    }

    /// <summary>A session endpoint, with the cookies and CSRF header a browser sends. The next session if it rotated.</summary>
    private async Task<(HttpStatusCode Status, Session? Next, string? Code)> SessionPostAsync(string path, Session session, object? body = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/" + path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
        request.Headers.Add("X-CSRF-Token", session.Csrf);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = text.Length > 0 ? JsonDocument.Parse(text).RootElement : default;
        return response.IsSuccessStatusCode
            ? (response.StatusCode, Parse(session.UserId, session.Phone, response, json), null)
            : (response.StatusCode, null, json.ValueKind == JsonValueKind.Object && json.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private async Task<HttpResponseMessage> SendAsync(Session session, HttpMethod method, string path, object? body = null)
    {
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        return await client.SendAsync(request);
    }

    private async Task SteppedUpAsync(Session session) =>
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(session.UserId, session.TokenId), 1, TimeSpan.FromMinutes(15));

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return text.Length > 0 && JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<string[]> PermissionsAsync(Session session)
    {
        using var response = await SendAsync(session, HttpMethod.Get, "/api/me/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];
    }

    /// <summary>An administrator, stepped up, who has just entered Alpha as support.</summary>
    private async Task<Session> InAlphaAsSupportAsync()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));
        await SteppedUpAsync(admin);
        using (var ticket = await SendAsync(admin, HttpMethod.Post, "/api/admin/institutions/alpha-temple/support"))
        {
            Assert.Equal(HttpStatusCode.NoContent, ticket.StatusCode);
        }

        var (status, inside, _) = await SessionPostAsync("switch-institution", admin, new { institution = "alpha-temple" });
        Assert.Equal(HttpStatusCode.OK, status);
        return inside!;
    }

    // ------------------------------------------------------------------ support mode (Y1-Y3)

    [Fact]
    public async Task AnAdministratorEntersAnyInstitutionAsSupport_AndHoldsEveryPermissionThere()
    {
        var inside = await InAlphaAsSupportAsync();

        Assert.Equal("alpha-temple", inside.Body.GetProperty("activeInstitution").GetString());
        // Listed, though not theirs, so the client can name where it is.
        Assert.Contains(inside.Body.GetProperty("institutions").EnumerateArray(), i => i.GetProperty("slug").GetString() == "alpha-temple");
        var token = new JsonWebToken(inside.AccessToken);
        Assert.Equal(database.AlphaInstitutionId, token.GetPayloadValue<int>(SethikaClaimTypes.InstitutionId));

        var held = await PermissionsAsync(inside);
        Assert.Contains("ACCESS.ROLES.EDIT", held);
        Assert.Contains("ACCESS.MEMBERS.EDIT", held);
        Assert.Contains("INSTITUTION.PROFILE.EDIT", held);
        Assert.DoesNotContain("PLATFORM.USERS.VIEW", held); // the platform's own hold only outside
    }

    [Fact]
    public async Task EnteringNeedsAStepUpForTheTicket_AndTheTicketIsSpentOnce()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));

        using (var noStepUp = await SendAsync(admin, HttpMethod.Post, "/api/admin/institutions/alpha-temple/support"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noStepUp.StatusCode);
            Assert.Equal("stepup_required", await CodeOf(noStepUp));
        }

        var (noTicket, _, code) = await SessionPostAsync("switch-institution", admin, new { institution = "alpha-temple" });
        Assert.Equal(HttpStatusCode.Forbidden, noTicket);
        Assert.Equal("institution_not_available", code);

        await SteppedUpAsync(admin);
        using (var ticket = await SendAsync(admin, HttpMethod.Post, "/api/admin/institutions/alpha-temple/support"))
        {
            Assert.Equal(HttpStatusCode.NoContent, ticket.StatusCode);
        }

        var (entered, inside, _) = await SessionPostAsync("switch-institution", admin, new { institution = "alpha-temple" });
        Assert.Equal(HttpStatusCode.OK, entered);

        var (left, personal, _) = await SessionPostAsync("switch-personal", inside!);
        Assert.Equal(HttpStatusCode.OK, left);
        var (again, _, againCode) = await SessionPostAsync("switch-institution", personal!, new { institution = "alpha-temple" });
        Assert.Equal(HttpStatusCode.Forbidden, again);
        Assert.Equal("institution_not_available", againCode);
    }

    [Fact]
    public async Task ARefreshKeepsSupportOnlyWhileThePermissionHolds()
    {
        var inside = await InAlphaAsSupportAsync();

        var (kept, refreshed, _) = await SessionPostAsync("refresh", inside);
        Assert.Equal(HttpStatusCode.OK, kept);
        Assert.Equal("alpha-temple", refreshed!.Body.GetProperty("activeInstitution").GetString());

        // The role that carried the permission is taken away: the next refresh leaves the institution.
        await ExecuteAsync(
            """
            UPDATE ur SET IsActive = 0
            FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @user AND r.Code = 'PLATFORM_ADMIN';
            """,
            new { user = inside.UserId });

        var (_, outside, _) = await SessionPostAsync("refresh", refreshed);
        Assert.Equal(JsonValueKind.Null, outside!.Body.GetProperty("activeInstitution").ValueKind);
        Assert.DoesNotContain(new JsonWebToken(outside.AccessToken).Claims, c => c.Type == SethikaClaimTypes.InstitutionId);
    }

    [Fact]
    public async Task ADenyOfTheSupportPermissionWins()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));
        await ExecuteAsync(
            """
            INSERT auth.UserPermissionOverrides (UserId, InstitutionId, PermissionId, Effect, Reason, CreatedBy)
            SELECT @user, NULL, p.Id, 'Deny', N'Test', @user FROM auth.Permissions p WHERE p.Code = 'PLATFORM.INSTITUTIONS.EXECUTE';
            UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @user;
            """,
            new { user = admin.UserId });

        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.vw_EffectivePermissions WHERE UserId = @user AND InstitutionId = @alpha;",
            new { user = admin.UserId, alpha = database.AlphaInstitutionId }));

        await SteppedUpAsync(admin);
        using var ticket = await SendAsync(admin, HttpMethod.Post, "/api/admin/institutions/alpha-temple/support");
        Assert.Equal(HttpStatusCode.Forbidden, ticket.StatusCode);
    }

    [Fact]
    public async Task SupportOutranksTheOwner()
    {
        var inside = await InAlphaAsSupportAsync();
        await SteppedUpAsync(inside);

        using var roles = await SendAsync(inside, HttpMethod.Get, "/api/institution/roles");
        var owner = JsonDocument.Parse(await roles.Content.ReadAsStringAsync()).RootElement.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "A_OWNER");
        Assert.True(owner.GetProperty("canManage").GetBoolean());

        using var levels = await SendAsync(inside, HttpMethod.Get, "/api/institution/role-levels");
        Assert.Contains(
            JsonDocument.Parse(await levels.Content.ReadAsStringAsync()).RootElement.EnumerateArray(),
            l => l.GetProperty("level").GetString() == "OWNER");

        using var edited = await SendAsync(inside, HttpMethod.Put, "/api/institution/roles/A_OWNER/permissions",
            new { permissions = OwnerPermissions, version = owner.GetProperty("version").GetBytesFromBase64() });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
    }

    private static readonly string[] OwnerPermissions =
        ["ACCESS.ROLES.VIEW", "ACCESS.ROLES.EDIT", "INSTITUTION.PROFILE.VIEW", "INSTITUTION.PROFILE.EDIT"];

    [Fact]
    public async Task SupportCannotTakeTheLastOwnerAway_ButCanTakeASecondOne()
    {
        var inside = await InAlphaAsSupportAsync();
        await SteppedUpAsync(inside);
        const string MakeOwner =
            """
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @user, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = 'A_OWNER';
            """;
        const string OwnersInForce =
            """
            SELECT COUNT(*) FROM auth.UserRoles ur
            JOIN auth.Roles r ON r.Id = ur.RoleId AND r.IsActive = 1
            JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId AND l.Rank = 1
            WHERE ur.InstitutionId = @alpha AND ur.IsActive = 1;
            """;
        Assert.Equal(0, await ScalarAsync<int>(OwnersInForce, new { alpha = database.AlphaInstitutionId }));

        var first = await LogInAsync(await AccountAsync());
        await ExecuteAsync(MakeOwner, new { user = first.UserId, alpha = database.AlphaInstitutionId });

        using (var last = await SendAsync(inside, HttpMethod.Delete, $"/api/institution/roles/A_OWNER/members/{first.UserId}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
            Assert.Equal("last_owner", await CodeOf(last));
        }

        Assert.Equal(1, await ScalarAsync<int>(OwnersInForce, new { alpha = database.AlphaInstitutionId }));

        // With a second owner, either may go.
        var second = await LogInAsync(await AccountAsync());
        await ExecuteAsync(MakeOwner, new { user = second.UserId, alpha = database.AlphaInstitutionId });

        using (var one = await SendAsync(inside, HttpMethod.Delete, $"/api/institution/roles/A_OWNER/members/{first.UserId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, one.StatusCode);
        }

        Assert.Equal(1, await ScalarAsync<int>(OwnersInForce, new { alpha = database.AlphaInstitutionId }));
    }

    [Fact]
    public async Task AReviewerCannotEnter_NorSeeTheAccounts()
    {
        var reviewer = await LogInAsync(await AccountAsync("PLATFORM_REVIEWER"));
        await SteppedUpAsync(reviewer);

        using var ticket = await SendAsync(reviewer, HttpMethod.Post, "/api/admin/institutions/alpha-temple/support");
        using var list = await SendAsync(reviewer, HttpMethod.Get, "/api/admin/institutions");
        using var create = await SendAsync(reviewer, HttpMethod.Post, "/api/admin/users", new { fullName = "X", phone = NewPhone(), language = "en" });

        Assert.Equal(HttpStatusCode.Forbidden, ticket.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task ADonorWithATicketKeyStillCannotEnter()
    {
        // The ticket is not the whole guarantee: the switch reads the permission too.
        var donor = await LogInAsync(await AccountAsync());
        await _redis.GetDatabase().StringSetAsync(RedisSupportTickets.KeyFor(donor.UserId, database.AlphaInstitutionId), 1, TimeSpan.FromMinutes(2));

        var (status, _, code) = await SessionPostAsync("switch-institution", donor, new { institution = "alpha-temple" });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("institution_not_available", code);
    }

    // ------------------------------------------------------------------ accounts (Y5-Y6)

    [Fact]
    public async Task AnAdministratorMakesAnAccount_PendingUntilItsFirstSignIn()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));
        await SteppedUpAsync(admin);
        var phone = NewPhone();
        _phones.Add(phone);

        using var created = await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new { fullName = "Made By Admin", phone = "0" + phone[3..], language = "si" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var row = await ScalarAsync<string>(
            """
            SELECT CONCAT(u.Status, '|', u.PhoneConfirmed, '|', IIF(u.PasswordHash IS NULL, 'nopass', 'pass'), '|', u.PreferredLanguage, '|',
                          (SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id AND r.Code = 'DONOR'))
            FROM auth.Users u WHERE u.PhoneNumber = @phone;
            """,
            new { phone });
        Assert.Equal("PendingVerification|0|nopass|si|1", row);

        using var again = await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new { fullName = "Again", phone, language = "en" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_registered", await CodeOf(again));

        using var found = await SendAsync(admin, HttpMethod.Get, "/api/admin/users?search=Made%20By%20Admin");
        Assert.Contains(JsonDocument.Parse(await found.Content.ReadAsStringAsync()).RootElement.EnumerateArray(),
            u => u.GetProperty("status").GetString() == "PendingVerification");
    }

    [Fact]
    public async Task SuspendingEndsTheAccountsSessions_AndNobodySuspendsThemselvesOrAnEqual()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));
        var otherAdmin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));
        var donor = await LogInAsync(await AccountAsync());
        await SteppedUpAsync(admin);

        // Warm the permission cache first, so the refusal below cannot come from a cold read.
        using (var before = await SendAsync(donor, HttpMethod.Get, "/api/bookings/mine"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        using (var suspend = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{donor.UserId}/suspend"))
        {
            Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);
        }

        // The access token issued before the suspension is still a valid JWT: it must hold nothing.
        using (var during = await SendAsync(donor, HttpMethod.Get, "/api/bookings/mine"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, during.StatusCode);
        }

        var (refreshed, _, _) = await SessionPostAsync("refresh", donor);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed);

        using (var back = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{donor.UserId}/reactivate"))
        {
            Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
        }

        using (var after = await SendAsync(donor, HttpMethod.Get, "/api/bookings/mine"))
        {
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        }

        Assert.Equal("Active", await ScalarAsync<string>("SELECT Status FROM auth.Users WHERE Id = @id;", new { id = donor.UserId }));

        using var self = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{admin.UserId}/suspend");
        using var equal = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{otherAdmin.UserId}/suspend");
        Assert.Equal("own_account", await CodeOf(self));
        Assert.Equal("rank_not_junior", await CodeOf(equal));
    }

    [Fact]
    public async Task PlatformRolesGoOnlyBelowOnesOwnRank_AndTakingTheLastOneEndsPlatformAccess()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));
        var donor = await LogInAsync(await AccountAsync());
        await SteppedUpAsync(admin);

        using (var equal = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{donor.UserId}/platform-roles", new { role = "PLATFORM_ADMIN" }))
        {
            Assert.Equal("rank_not_junior", await CodeOf(equal));
        }

        using (var granted = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{donor.UserId}/platform-roles", new { role = "PLATFORM_REVIEWER" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        }

        Assert.Equal("PlatformStaff", await ScalarAsync<string>("SELECT UserType FROM auth.Users WHERE Id = @id;", new { id = donor.UserId }));
        var reviewer = await LogInAsync(donor.Phone);
        Assert.True(new JsonWebToken(reviewer.AccessToken).GetPayloadValue<bool>(SethikaClaimTypes.IsPlatform));
        Assert.Contains("ONBOARDING.REQUESTS.VIEW", await PermissionsAsync(reviewer));

        using (var revoked = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/users/{donor.UserId}/platform-roles/PLATFORM_REVIEWER"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        Assert.Equal("Donor", await ScalarAsync<string>("SELECT UserType FROM auth.Users WHERE Id = @id;", new { id = donor.UserId }));
        // The stamp rotated: the token that said "platform" cannot be refreshed into another.
        var (status, _, _) = await SessionPostAsync("refresh", reviewer);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task TheInstitutionsListNamesTheOwnerAndFindsBySearch()
    {
        var admin = await LogInAsync(await AccountAsync("PLATFORM_ADMIN"));

        using var all = await SendAsync(admin, HttpMethod.Get, "/api/admin/institutions?search=alpha");
        var rows = JsonDocument.Parse(await all.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();

        var alpha = Assert.Single(rows, r => r.GetProperty("slug").GetString() == "alpha-temple");
        Assert.Equal("Active", alpha.GetProperty("status").GetString());
        Assert.DoesNotContain(rows, r => r.GetProperty("slug").GetString() == "beta-temple");

        using var wildcard = await SendAsync(admin, HttpMethod.Get, "/api/admin/institutions?search=%25");
        Assert.Empty(JsonDocument.Parse(await wildcard.Content.ReadAsStringAsync()).RootElement.EnumerateArray());
    }
}
