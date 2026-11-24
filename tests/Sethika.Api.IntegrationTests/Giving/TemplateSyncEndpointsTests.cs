using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Giving;

/// <summary>
/// Plan 09, Q4: the platform action "bring this institution's roles up to their templates". Alpha is set
/// up as an institution approved before Phase D: its roles were copied from the templates without the
/// GIVING permissions, and it has no GIVING module row.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class TemplateSyncEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "template-sync-test-signing-key-32-bytes!!";
    private const string Sync = "/api/admin/institutions/alpha-temple/sync-templates";

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;
    private Guid _admin;
    private Guid _alphaOwner;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);

        await ExecuteAsync(
            """
            -- OWNER, MANAGER and STAFF copied as an approval before Phase D copied them: no GIVING grant,
            -- and no LEADER role (the institution predates it). With TemplateRoleId, as approval sets it.
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, TemplateRoleId)
            SELECT @alpha, 'Institution', t.RoleLevelId, t.Code, t.Name, 0, t.Id
            FROM auth.Roles t
            WHERE t.IsTemplate = 1 AND t.InstitutionId IS NULL AND t.Code IN ('OWNER', 'MANAGER', 'STAFF');

            INSERT auth.RolePermissions (RoleId, PermissionId)
            SELECT r.Id, tp.PermissionId
            FROM auth.Roles r
            JOIN auth.RolePermissions tp ON tp.RoleId = r.TemplateRoleId
            JOIN auth.Permissions p      ON p.Id = tp.PermissionId
            WHERE r.InstitutionId = @alpha AND p.Code NOT LIKE 'GIVING.%';
            """,
            new { alpha = database.AlphaInstitutionId });

        _admin = await UserAsync("PlatformStaff");
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'PLATFORM_ADMIN' AND r.InstitutionId IS NULL;",
            new { id = _admin });

        _alphaOwner = await UserAsync("InstitutionStaff");
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = 'OWNER';",
            new { id = _alphaOwner, alpha = database.AlphaInstitutionId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        });
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync("DELETE FROM audit.AuditLogs WHERE Action = 'SyncTemplates' AND UserId IN @users; DELETE FROM auth.UserRoles WHERE UserId IN @users; DELETE FROM auth.Users WHERE Id IN @users;", new { users = _users });
        await _redis.DisposeAsync();
        await _host.DisposeAsync();
    }

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private async Task<Guid> UserAsync(string userType)
    {
        var id = Guid.CreateVersion7();
        var phone = "+9475" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        await ExecuteAsync(
            "INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp) VALUES (@id, @userType, N'Sync Test', @phone, 1, 'en', 'Active', 'stamp');",
            new { id, userType, phone });
        _users.Add(id);
        return id;
    }

    private async Task<HttpClient> ClientAsync(Guid userId, int? institutionId, bool steppedUp)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: institutionId is null));
        if (steppedUp)
        {
            await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(userId, tokens.Access.JwtId), 1, TimeSpan.FromMinutes(15));
        }

        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return client;
    }

    private async Task<HttpResponseMessage> SyncAsync(bool steppedUp = true)
    {
        using var client = await ClientAsync(_admin, null, steppedUp);
        return await client.PostAsync(Sync, null);
    }

    private Task<int> GrantsAsync(string roleCode, string pattern) =>
        ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.RolePermissions rp
            JOIN auth.Roles r ON r.Id = rp.RoleId JOIN auth.Permissions p ON p.Id = rp.PermissionId
            WHERE r.InstitutionId = @alpha AND r.Code = @roleCode AND p.Code LIKE @pattern;
            """,
            new { alpha = database.AlphaInstitutionId, roleCode, pattern });

    private Task<int> TemplateGrantsAsync(string roleCode, string pattern) =>
        ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.RolePermissions rp
            JOIN auth.Roles r ON r.Id = rp.RoleId JOIN auth.Permissions p ON p.Id = rp.PermissionId
            WHERE r.InstitutionId IS NULL AND r.IsTemplate = 1 AND r.Code = @roleCode AND p.Code LIKE @pattern;
            """,
            new { roleCode, pattern });

    [Fact]
    public async Task TheAction_AddsWhatTheTemplatesGained_AndTheOwnerCanReachGiving()
    {
        using (var before = await ClientAsync(_alphaOwner, database.AlphaInstitutionId, steppedUp: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await before.GetAsync("/api/institution/donations")).StatusCode);
        }

        using var response = await SyncAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonElement.Parse(await response.Content.ReadAsStringAsync());

        var ownerGiving = await TemplateGrantsAsync("OWNER", "GIVING.%");
        var managerGiving = await TemplateGrantsAsync("MANAGER", "GIVING.%");
        var staffGiving = await TemplateGrantsAsync("STAFF", "GIVING.%");
        var leader = await TemplateGrantsAsync("LEADER", "%");
        Assert.True(ownerGiving > managerGiving && managerGiving > staffGiving && staffGiving > 0);

        Assert.Equal(ownerGiving, await GrantsAsync("OWNER", "GIVING.%"));
        Assert.Equal(managerGiving, await GrantsAsync("MANAGER", "GIVING.%"));
        Assert.Equal(staffGiving, await GrantsAsync("STAFF", "GIVING.%"));
        Assert.Equal(leader, await GrantsAsync("LEADER", "%")); // the role it lacked, copied with its grants
        Assert.Equal(1, body.GetProperty("roles").GetInt32());
        Assert.Equal(ownerGiving + managerGiving + staffGiving + leader, body.GetProperty("grants").GetInt32());

        // Its core modules it had no row for are on now; BOOKINGS, which it had, is counted out.
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM org.InstitutionModules im JOIN auth.Modules m ON m.Id = im.ModuleId WHERE im.InstitutionId = @alpha AND m.Code = 'GIVING' AND im.IsEnabled = 1;",
            new { alpha = database.AlphaInstitutionId }));
        Assert.True(body.GetProperty("modules").GetInt32() >= 1);

        using (var after = await ClientAsync(_alphaOwner, database.AlphaInstitutionId, steppedUp: false))
        {
            Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/api/institution/donations")).StatusCode);
        }

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLogs WHERE Action = 'SyncTemplates' AND UserId = @admin AND InstitutionId = @alpha;",
            new { admin = _admin, alpha = database.AlphaInstitutionId }));

        // Again: nothing more to add.
        using var again = await SyncAsync();
        var second = JsonElement.Parse(await again.Content.ReadAsStringAsync());
        Assert.Equal(0, second.GetProperty("modules").GetInt32() + second.GetProperty("roles").GetInt32() + second.GetProperty("grants").GetInt32());
    }

    [Fact]
    public async Task AGrantTheInstitutionTookAway_StaysAway_AndASwitchedOffModuleStaysOff()
    {
        // The owner removes the staff's donor book view on purpose: it is now in the role's history.
        await ExecuteAsync(
            """
            DELETE rp FROM auth.RolePermissions rp
            JOIN auth.Roles r ON r.Id = rp.RoleId JOIN auth.Permissions p ON p.Id = rp.PermissionId
            WHERE r.InstitutionId = @alpha AND r.Code = 'STAFF' AND p.Code = 'BOOKINGS.SPONSORS.VIEW';

            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT @alpha, m.Id, 0, 'Manual', @admin FROM auth.Modules m WHERE m.Code = 'GIVING';
            """,
            new { alpha = database.AlphaInstitutionId, admin = _admin });

        using var response = await SyncAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(0, await GrantsAsync("STAFF", "BOOKINGS.SPONSORS.VIEW"));
        Assert.True(await GrantsAsync("STAFF", "GIVING.%") > 0);
        Assert.False(await ScalarAsync<bool>(
            "SELECT im.IsEnabled FROM org.InstitutionModules im JOIN auth.Modules m ON m.Id = im.ModuleId WHERE im.InstitutionId = @alpha AND m.Code = 'GIVING';",
            new { alpha = database.AlphaInstitutionId }));
    }

    [Fact]
    public async Task TheAction_NeedsAStepUp_AndThePlatformAdminsPermission()
    {
        using (var notSteppedUp = await SyncAsync(steppedUp: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, notSteppedUp.StatusCode);
            Assert.Contains("stepup_required", await notSteppedUp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var reviewer = await UserAsync("PlatformStaff");
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'PLATFORM_REVIEWER' AND r.InstitutionId IS NULL;",
            new { id = reviewer });
        using (var client = await ClientAsync(reviewer, null, steppedUp: true))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Sync, null)).StatusCode);
        }

        Assert.Equal(0, await GrantsAsync("OWNER", "GIVING.%"));

        using var admin = await ClientAsync(_admin, null, steppedUp: true);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/admin/institutions/no-such-place/sync-templates", null)).StatusCode);
    }
}
