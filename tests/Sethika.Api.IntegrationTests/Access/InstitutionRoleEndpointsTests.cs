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
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Custom roles over HTTP (plan 5.9): the three A15 rules - rank strictly more junior, inside the
/// ceiling, held by the actor - against real SQL Server and Redis.
/// </summary>
/// <remarks>
/// Alpha gets three roles here: an owner role with every institution permission, a manager role
/// with everything except <c>BOOKINGS.CALENDAR.APPROVE</c> and <c>INSTITUTION.PROFILE.EDIT</c> - so
/// there is something in STAFF's ceiling the manager does not hold - and a staff role with the two
/// VIEWs a member starts with. The step-up these endpoints need is written straight into Redis:
/// the step-up flow has its own tests, and these are about roles.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class InstitutionRoleEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "institution-role-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";

    private static readonly string[] OwnerPermissions =
    [
        "INSTITUTION.PROFILE.VIEW", "INSTITUTION.PROFILE.EDIT", "BOOKINGS.CALENDAR.VIEW", "BOOKINGS.CALENDAR.APPROVE",
        "ACCESS.ROLES.VIEW", "ACCESS.ROLES.CREATE", "ACCESS.ROLES.EDIT", "ACCESS.MEMBERS.VIEW", "ACCESS.MEMBERS.EDIT",
    ];

    private static readonly string[] ManagerPermissions =
    [
        "INSTITUTION.PROFILE.VIEW", "BOOKINGS.CALENDAR.VIEW",
        "ACCESS.ROLES.VIEW", "ACCESS.ROLES.CREATE", "ACCESS.ROLES.EDIT", "ACCESS.MEMBERS.VIEW", "ACCESS.MEMBERS.EDIT",
    ];

    private static readonly string[] StaffPermissions = ["INSTITUTION.PROFILE.VIEW", "BOOKINGS.CALENDAR.VIEW"];

    /// <summary>A staff role the owner gave APPROVE - inside STAFF's ceiling, but not the manager's to give.</summary>
    private static readonly string[] ApproverPermissions = ["BOOKINGS.CALENDAR.VIEW", "BOOKINGS.CALENDAR.APPROVE"];

    private static readonly string[] StaffWithApprove = [.. StaffPermissions, "BOOKINGS.CALENDAR.APPROVE"];
    private static readonly string[] ApproveOnly = ["BOOKINGS.CALENDAR.APPROVE"];

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
            WHERE m.Code = 'ACCESS' OR m.Code = 'INSTITUTION';
            """,
            new { alpha = database.AlphaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        await CreateRoleAsync("T_OWNER", "OWNER", OwnerPermissions);
        await CreateRoleAsync("T_MANAGER", "MANAGER", ManagerPermissions);
        await CreateRoleAsync("T_STAFF", "STAFF", StaffPermissions);

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

            -- What these users made or granted points back at them: CreatedBy, GrantedBy, AssignedBy.
            DECLARE @made TABLE (Id INT);
            INSERT @made SELECT Id FROM auth.Roles WHERE CreatedBy IN (SELECT Id FROM @ids);

            UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.RefreshTokens   WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.LoginHistory    WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.RolePermissions WHERE GrantedBy IN (SELECT Id FROM @ids) OR RoleId IN (SELECT Id FROM @made);
            DELETE FROM auth.UserRoles       WHERE UserId IN (SELECT Id FROM @ids) OR AssignedBy IN (SELECT Id FROM @ids)
                                                OR RoleId IN (SELECT Id FROM @made);
            DELETE FROM auth.Roles           WHERE Id IN (SELECT Id FROM @made);
            DELETE FROM auth.Users           WHERE Id     IN (SELECT Id FROM @ids);
            DELETE FROM core.OutboxMessages  WHERE EventType = 'RolePermissionsChanged';
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

    /// <summary>A signed-in member of Alpha.</summary>
    private sealed record Member(Guid UserId, string AccessToken, string TokenId);

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

    private Task CreateRoleAsync(string code, string level, string[] permissions, int? institutionId = null) =>
        ExecuteAsync(
            """
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            SELECT @institutionId, 'Institution', l.Id, @code, @code, 0 FROM auth.RoleLevels l WHERE l.Code = @level;

            INSERT auth.RolePermissions (RoleId, PermissionId)
            SELECT r.Id, p.Id FROM auth.Roles r CROSS JOIN auth.Permissions p
            WHERE r.InstitutionId = @institutionId AND r.Code = @code AND p.Code IN @permissions;
            """,
            new { institutionId = institutionId ?? database.AlphaInstitutionId, code, level, permissions });

    /// <summary>A user holding <paramref name="roleCode"/> in Alpha (or nothing), signed in.</summary>
    private async Task<Member> MemberAsync(string? roleCode)
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await ExecuteAsync(
            """
            DECLARE @id UNIQUEIDENTIFIER = NEWID();
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (@id, 'InstitutionStaff', N'Role Test', @phone, 1, @hash, 'en', 'Active');

            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = @roleCode;
            """,
            new { phone, hash, alpha = database.AlphaInstitutionId, roleCode });

        using var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
        var jwt = new JsonWebToken(token);
        return new Member(Guid.Parse(jwt.GetClaim(SethikaClaimTypes.UserId).Value), token, jwt.Id);
    }

    /// <summary>As if <paramref name="member"/> had just stepped up (plan A5).</summary>
    private async Task SteppedUpAsync(Member member) =>
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(member.UserId, member.TokenId), 1, TimeSpan.FromMinutes(15));

    private HttpClient ClientFor(Member member)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        return client;
    }

    private async Task<HttpResponseMessage> SendAsync(Member member, HttpMethod method, string path, object? body = null)
    {
        using var client = ClientFor(member);
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> CreateAsync(Member actor, string code, string level, params string[] permissions) =>
        SendAsync(actor, HttpMethod.Post, "/api/institution/roles", new { code, name = "Test role", level, permissions });

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, params string[] permissions)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal(code, body.GetProperty("code").GetString());

        if (permissions.Length > 0)
        {
            Assert.Equal(permissions, body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        }
    }

    private async Task<string[]> MyPermissionsAsync(Member member)
    {
        using var response = await SendAsync(member, HttpMethod.Get, "/api/me/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await BodyOf(response)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];
    }

    private Task<int> RoleCountAsync(string code) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM auth.Roles WHERE InstitutionId = @alpha AND Code = @code;", new { alpha = database.AlphaInstitutionId, code });

    // ------------------------------------------------------------------ what may be granted

    [Fact]
    public async Task RoleLevelsOffersOnlyMoreJuniorLevels_WithWhatTheActorMayGrant()
    {
        var manager = await MemberAsync("T_MANAGER");
        var owner = await MemberAsync("T_OWNER");

        using var forManager = await SendAsync(manager, HttpMethod.Get, "/api/institution/role-levels");
        using var forOwner = await SendAsync(owner, HttpMethod.Get, "/api/institution/role-levels");

        Assert.Equal(HttpStatusCode.OK, forManager.StatusCode);
        var managerLevels = (await BodyOf(forManager)).EnumerateArray().ToList();
        var staff = Assert.Single(managerLevels);
        Assert.Equal("STAFF", staff.GetProperty("level").GetString());

        // STAFF's ceiling is the four VIEWs and calendar APPROVE; the manager does not hold APPROVE.
        Assert.Equal(
            ["ACCESS.MEMBERS.VIEW", "ACCESS.ROLES.VIEW", "BOOKINGS.CALENDAR.VIEW", "INSTITUTION.PROFILE.VIEW"],
            staff.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var ownerLevels = (await BodyOf(forOwner)).EnumerateArray().Select(l => l.GetProperty("level").GetString()).ToList();
        Assert.Equal(["MANAGER", "STAFF"], ownerLevels);
    }

    // ------------------------------------------------------------------ the three rules

    [Fact]
    public async Task AManagerCannotCreateAManagerOrOwnerRole()
    {
        var manager = await MemberAsync("T_MANAGER");

        using var asManager = await CreateAsync(manager, "DEPUTY", "MANAGER", "BOOKINGS.CALENDAR.VIEW");
        using var asOwner = await CreateAsync(manager, "CO_OWNER", "OWNER", "BOOKINGS.CALENDAR.VIEW");

        await AssertRefusedAsync(asManager, HttpStatusCode.Forbidden, "rank_not_junior");
        await AssertRefusedAsync(asOwner, HttpStatusCode.Forbidden, "rank_not_junior");
        Assert.Equal(0, await RoleCountAsync("DEPUTY"));
        Assert.Equal(0, await RoleCountAsync("CO_OWNER"));
    }

    [Fact]
    public async Task AManagerCreatesAStaffRole_AndTheChangeIsRecorded()
    {
        var manager = await MemberAsync("T_MANAGER");

        using var response = await CreateAsync(manager, "cashier", "STAFF", "BOOKINGS.CALENDAR.VIEW", "INSTITUTION.PROFILE.VIEW");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("CASHIER", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("id", out _));

        Assert.Equal(2, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.RolePermissions rp
            JOIN auth.Roles r ON r.Id = rp.RoleId JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
            WHERE r.InstitutionId = @alpha AND r.Code = 'CASHIER' AND l.Code = 'STAFF';
            """,
            new { alpha = database.AlphaInstitutionId }));

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'RolePermissionsChanged' AND Payload LIKE '%\"CASHIER\"%Created%';",
            new { }));
    }

    [Fact]
    public async Task NothingOutsideTheLevelsCeiling()
    {
        // The manager holds ACCESS.ROLES.CREATE, but no STAFF role may.
        var manager = await MemberAsync("T_MANAGER");

        using var response = await CreateAsync(manager, "CLERK", "STAFF", "BOOKINGS.CALENDAR.VIEW", "ACCESS.ROLES.CREATE");

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "outside_ceiling", "ACCESS.ROLES.CREATE");
        Assert.Equal(0, await RoleCountAsync("CLERK"));
    }

    [Fact]
    public async Task NothingTheActorDoesNotHold()
    {
        // APPROVE is inside STAFF's ceiling, but this manager does not hold it.
        var manager = await MemberAsync("T_MANAGER");

        using var response = await CreateAsync(manager, "APPROVER", "STAFF", "BOOKINGS.CALENDAR.APPROVE");

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "not_held", "BOOKINGS.CALENDAR.APPROVE");
        Assert.Equal(0, await RoleCountAsync("APPROVER"));
    }

    [Fact]
    public async Task UnknownPermissionsAndDuplicateCodesAreRefused()
    {
        var manager = await MemberAsync("T_MANAGER");

        using var unknown = await CreateAsync(manager, "GHOST", "STAFF", "BOOKINGS.CALENDAR.FLY");
        using var duplicate = await CreateAsync(manager, "T_STAFF", "STAFF", "BOOKINGS.CALENDAR.VIEW");

        await AssertRefusedAsync(unknown, HttpStatusCode.BadRequest, "unknown_permission", "BOOKINGS.CALENDAR.FLY");
        await AssertRefusedAsync(duplicate, HttpStatusCode.Conflict, "role_exists");
    }

    [Fact]
    public async Task StaffCannotReachTheRoleEndpointsAtAll()
    {
        var staff = await MemberAsync("T_STAFF");

        using var levels = await SendAsync(staff, HttpMethod.Get, "/api/institution/role-levels");
        using var create = await CreateAsync(staff, "MINE", "STAFF", "BOOKINGS.CALENDAR.VIEW");

        // Refused by [HasPermission], whose 403 has no body - not by the handler's rank check, which
        // would also say 403, but only after letting the request in.
        Assert.Equal(HttpStatusCode.Forbidden, levels.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Empty(await create.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ editing a role

    [Fact]
    public async Task EditingARoleNeedsAStepUp()
    {
        var owner = await MemberAsync("T_OWNER");

        using var response = await SendAsync(owner, HttpMethod.Put, "/api/institution/roles/T_STAFF/permissions",
            new { permissions = StaffPermissions });

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "stepup_required");
    }

    [Fact]
    public async Task EditingARoleChangesWhatItsHoldersMayDoAtOnce()
    {
        var owner = await MemberAsync("T_OWNER");
        var member = await MemberAsync("T_STAFF");
        await SteppedUpAsync(owner);
        Assert.DoesNotContain("BOOKINGS.CALENDAR.APPROVE", await MyPermissionsAsync(member)); // now cached

        using var response = await SendAsync(owner, HttpMethod.Put, "/api/institution/roles/T_STAFF/permissions",
            new { permissions = StaffWithApprove });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("BOOKINGS.CALENDAR.APPROVE", await MyPermissionsAsync(member));
    }

    [Fact]
    public async Task OnlyWhatIsAddedMustBeHeld()
    {
        // The owner gave this staff role APPROVE, which the manager does not hold. The manager may
        // still take VIEW away from it: removing is within anyone who may manage the role, and the
        // APPROVE that stays is not being granted by the manager.
        await CreateRoleAsync("T_APPROVER", "STAFF", ApproverPermissions);
        var manager = await MemberAsync("T_MANAGER");
        await SteppedUpAsync(manager);

        using var narrowed = await SendAsync(manager, HttpMethod.Put, "/api/institution/roles/T_APPROVER/permissions",
            new { permissions = ApproveOnly });

        Assert.Equal(HttpStatusCode.NoContent, narrowed.StatusCode);
    }

    [Fact]
    public async Task AManagerCannotEditAManagerRole_NorReachAnotherInstitutionsRole()
    {
        await CreateRoleAsync("BETA_ONLY", "STAFF", StaffPermissions, database.BetaInstitutionId);
        var manager = await MemberAsync("T_MANAGER");
        await SteppedUpAsync(manager);

        using var ownRank = await SendAsync(manager, HttpMethod.Put, "/api/institution/roles/T_MANAGER/permissions",
            new { permissions = ManagerPermissions });
        using var elsewhere = await SendAsync(manager, HttpMethod.Put, "/api/institution/roles/BETA_ONLY/permissions",
            new { permissions = StaffPermissions });

        await AssertRefusedAsync(ownRank, HttpStatusCode.Forbidden, "rank_not_junior");
        await AssertRefusedAsync(elsewhere, HttpStatusCode.NotFound, "role_not_found");
    }

    // ------------------------------------------------------------------ assignments

    [Fact]
    public async Task AssigningAndRevokingChangeTheMembersPermissionsAtOnce()
    {
        await CreateRoleAsync("T_APPROVER", "STAFF", ApproverPermissions);
        var owner = await MemberAsync("T_OWNER");
        var member = await MemberAsync("T_STAFF");
        await SteppedUpAsync(owner);
        Assert.DoesNotContain("BOOKINGS.CALENDAR.APPROVE", await MyPermissionsAsync(member)); // now cached

        using (var assigned = await SendAsync(owner, HttpMethod.Post, "/api/institution/roles/T_APPROVER/members", new { user = member.UserId }))
        {
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        }

        Assert.Contains("BOOKINGS.CALENDAR.APPROVE", await MyPermissionsAsync(member));

        using (var revoked = await SendAsync(owner, HttpMethod.Delete, $"/api/institution/roles/T_APPROVER/members/{member.UserId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        Assert.DoesNotContain("BOOKINGS.CALENDAR.APPROVE", await MyPermissionsAsync(member));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'RolePermissionsChanged' AND Payload LIKE '%\"T_APPROVER\"%';",
            new { }));
    }

    [Fact]
    public async Task AssigningAndRevokingNeedAStepUp()
    {
        var owner = await MemberAsync("T_OWNER");
        var member = await MemberAsync("T_STAFF");

        using var assign = await SendAsync(owner, HttpMethod.Post, "/api/institution/roles/T_STAFF/members", new { user = member.UserId });
        using var revoke = await SendAsync(owner, HttpMethod.Delete, $"/api/institution/roles/T_STAFF/members/{member.UserId}");

        await AssertRefusedAsync(assign, HttpStatusCode.Forbidden, "stepup_required");
        await AssertRefusedAsync(revoke, HttpStatusCode.Forbidden, "stepup_required");
    }

    [Fact]
    public async Task NobodyAssignsOrRevokesTheirOwnRoles()
    {
        var owner = await MemberAsync("T_OWNER");
        await SteppedUpAsync(owner);

        using var assign = await SendAsync(owner, HttpMethod.Post, "/api/institution/roles/T_STAFF/members", new { user = owner.UserId });
        using var revoke = await SendAsync(owner, HttpMethod.Delete, $"/api/institution/roles/T_OWNER/members/{owner.UserId}");

        await AssertRefusedAsync(assign, HttpStatusCode.Forbidden, "own_roles");
        await AssertRefusedAsync(revoke, HttpStatusCode.Forbidden, "own_roles");
    }

    [Fact]
    public async Task AManagerCannotHandOutAManagerRole()
    {
        var manager = await MemberAsync("T_MANAGER");
        var member = await MemberAsync("T_STAFF");
        await SteppedUpAsync(manager);

        using var response = await SendAsync(manager, HttpMethod.Post, "/api/institution/roles/T_MANAGER/members", new { user = member.UserId });

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "rank_not_junior");
        Assert.DoesNotContain("ACCESS.ROLES.CREATE", await MyPermissionsAsync(member));
    }

    [Fact]
    public async Task OnlyExistingMembersAreGivenRoles()
    {
        // Joining an institution is an invitation someone accepts, not something done to an account.
        var owner = await MemberAsync("T_OWNER");
        var outsider = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);

        using var response = await SendAsync(owner, HttpMethod.Post, "/api/institution/roles/T_STAFF/members", new { user = outsider.UserId });

        await AssertRefusedAsync(response, HttpStatusCode.NotFound, "member_not_found");
    }
}
