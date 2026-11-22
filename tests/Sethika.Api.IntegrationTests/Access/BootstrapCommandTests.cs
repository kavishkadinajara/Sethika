using System.Globalization;
using System.Net;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Sethika.Api.Bootstrap;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// The first platform administrator (plan Q5), through the same entry point
/// <c>dotnet run -- bootstrap</c> uses, against real SQL Server.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class BootstrapCommandTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "bootstrap-command-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";

    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        // The reseed removes every role assignment, so no test starts with an administrator.
        await database.SeedAsync();

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
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
                INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;

                UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

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

    private sealed class Admin
    {
        public string UserType { get; init; } = null!;
        public bool PhoneConfirmed { get; init; }
        public bool MfaRequired { get; init; }
        public string PasswordHash { get; init; } = null!;
        public int PermissionsVersion { get; init; }
        public int AdminRoles { get; init; }
    }

    private string NewPhone()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string? phone, string? password = Password, string? fullName = "Platform Admin")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:PhoneNumber"] = phone,
                ["Bootstrap:Password"] = password,
                ["Bootstrap:FullName"] = fullName,
            })
            .Build();

        await using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = await BootstrapCommand.RunAsync(_host.Services, configuration, output);
        return (exitCode, output.ToString());
    }

    private async Task<Admin?> ReadAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleOrDefaultAsync<Admin>(
            """
            SELECT u.UserType, u.PhoneConfirmed, u.MfaRequired, u.PasswordHash, u.PermissionsVersion,
                   (SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId
                    WHERE ur.UserId = u.Id AND ur.IsActive = 1 AND r.Code = 'PLATFORM_ADMIN' AND r.InstitutionId IS NULL) AS AdminRoles
            FROM auth.Users u WHERE u.PhoneNumber = @phone;
            """,
            new { phone });
    }

    private async Task InsertUserAsync(string phone, string userType)
    {
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (NEWID(), @userType, N'Existing Account', @phone, 1, @hash, 'en', 'Active');
            """,
            new { phone, userType, hash });
    }

    // ------------------------------------------------------------------ the first administrator

    [Fact]
    public async Task CreatesTheFirstAdministrator_WhoSignsInAsPlatformStaff()
    {
        var phone = NewPhone();

        var (exitCode, output) = await RunAsync("0" + phone[3..]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);

        var admin = await ReadAsync(phone);
        Assert.NotNull(admin);
        Assert.Equal("PlatformStaff", admin.UserType);
        Assert.True(admin.PhoneConfirmed);
        Assert.True(admin.MfaRequired);
        Assert.Equal(1, admin.AdminRoles);

        using var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
        Assert.Equal("true", new JsonWebToken(token).GetClaim(SethikaClaimTypes.IsPlatform).Value, ignoreCase: true);
    }

    [Fact]
    public async Task RunningItAgainChangesNothing_NotEvenThePassword()
    {
        var phone = NewPhone();
        await RunAsync(phone);
        var first = await ReadAsync(phone);

        var (exitCode, output) = await RunAsync(phone, password: "Another!Pass2026");

        Assert.Equal(0, exitCode);
        Assert.Contains("Already", output, StringComparison.Ordinal);

        var second = await ReadAsync(phone);
        Assert.Equal(first!.PasswordHash, second!.PasswordHash);
        Assert.Equal(1, second.AdminRoles);
        Assert.Equal(first.PermissionsVersion, second.PermissionsVersion);
    }

    [Fact]
    public async Task MakesOnlyTheFirstAdministrator()
    {
        var first = NewPhone();
        var second = NewPhone();
        await RunAsync(first);

        var (exitCode, output) = await RunAsync(second);

        Assert.Equal(1, exitCode);
        Assert.Contains("another active platform administrator", output, StringComparison.Ordinal);
        Assert.Null(await ReadAsync(second));
    }

    [Fact]
    public async Task ASuspendedAdministratorDoesNotBlockRecovery()
    {
        var first = NewPhone();
        var second = NewPhone();
        await RunAsync(first);

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE auth.Users SET Status = 'Suspended' WHERE PhoneNumber = @first;", new { first });
        }

        var (exitCode, _) = await RunAsync(second);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, (await ReadAsync(second))!.AdminRoles);
    }

    // ------------------------------------------------------------------ existing accounts

    [Fact]
    public async Task ADonorIsNotTurnedIntoAnAdministrator()
    {
        var phone = NewPhone();
        await InsertUserAsync(phone, "Donor");

        var (exitCode, output) = await RunAsync(phone);

        Assert.Equal(1, exitCode);
        Assert.Contains("not converted", output, StringComparison.Ordinal);

        var account = await ReadAsync(phone);
        Assert.Equal("Donor", account!.UserType);
        Assert.Equal(0, account.AdminRoles);
    }

    [Fact]
    public async Task AnExistingPlatformAccountIsGivenTheRole_AndItsPermissionsVersionMoves()
    {
        var phone = NewPhone();
        await InsertUserAsync(phone, "PlatformStaff");
        var before = await ReadAsync(phone);

        var (exitCode, output) = await RunAsync(phone, password: "ignored");

        Assert.Equal(0, exitCode);
        Assert.Contains("now the administrator", output, StringComparison.Ordinal);

        var after = await ReadAsync(phone);
        Assert.Equal(1, after!.AdminRoles);
        Assert.Equal(before!.PermissionsVersion + 1, after.PermissionsVersion);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
    }

    // ------------------------------------------------------------------ bad configuration

    [Fact]
    public async Task AWeakPasswordCreatesNothing()
    {
        var phone = NewPhone();

        var (exitCode, output) = await RunAsync(phone, password: "short");

        Assert.Equal(1, exitCode);
        Assert.Contains("password policy", output, StringComparison.Ordinal);
        Assert.Null(await ReadAsync(phone));
    }

    [Theory]
    [InlineData(null, "Platform Admin", "PhoneNumber")]
    [InlineData("not a phone", "Platform Admin", "PhoneNumber")]
    [InlineData("0112345678", "Platform Admin", "PhoneNumber")] // a landline: no step-up code could reach it (QA DEF-002)
    [InlineData("0771234567", null, "FullName")]
    public async Task MissingDetailsCreateNothing(string? phone, string? fullName, string named)
    {
        var (exitCode, output) = await RunAsync(phone, fullName: fullName);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bootstrap:" + named, output, StringComparison.Ordinal);

        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE r.Code = 'PLATFORM_ADMIN';"));
    }
}
