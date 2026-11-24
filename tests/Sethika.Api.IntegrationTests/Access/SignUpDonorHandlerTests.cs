using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Donor signup end to end: a real code sent and verified through the real OTP service and Redis, then
/// the account written to the real database, then signed in to.
/// </summary>
/// <remarks>
/// Only the SMS gateway is replaced, by one that records the message so the code can be read back.
/// Everything the handler relies on for correctness - the ticket's single use, the unique indexes,
/// the transaction - is the real thing.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class SignUpDonorHandlerTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "sign-up-test-signing-key-at-least-32-bytes-long!!";
    private const string Password = "Dana!2026Sethika";

    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;
    private string _terms = null!;
    private string _privacy = null!;

    private string[] BothDocuments => [_terms, _privacy];

    private sealed class RecordingSmsSender(ConcurrentQueue<(string Phone, string Message)> sent) : ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, string? dedupKey = null, CancellationToken cancellationToken = default)
        {
            sent.Enqueue((phoneNumber, message));
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
                services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms)));
        });

        await using var connection = new SqlConnection(database.ConnectionString);
        var hashes = (await connection.QueryAsync<(string Code, string Hash)>(
            "SELECT Code, ContentSha256 FROM core.LegalDocuments WHERE IsCurrent = 1 AND Language = 'en';"))
            .ToDictionary(d => d.Code, d => d.Hash, StringComparer.Ordinal);

        _terms = hashes["TERMS"];
        _privacy = hashes["PRIVACY"];
    }

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
                INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;

                DELETE FROM core.OutboxMessages WHERE AggregateType = 'User'
                    AND AggregateId IN (SELECT LOWER(CONVERT(VARCHAR(36), Id)) FROM @ids);
                DELETE FROM auth.UserConsents  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.DonorProfiles WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

        await _host.DisposeAsync();
    }

    private string NewPhone()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private static string LocalSpelling(string e164) => "0" + e164[3..];

    /// <summary>Requests and verifies a real code for <paramref name="phone"/>, returning the ticket.</summary>
    private async Task<string> VerifiedTicketAsync(string phone)
    {
        using var scope = _host.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        Assert.True((await otp.SendAsync(phone, OtpPurpose.Signup, "en")).WasSent);

        var message = _sms.Last(m => m.Phone == phone).Message;
        var verify = await otp.VerifyAsync(phone, OtpPurpose.Signup, new string([.. message.Where(char.IsDigit)]));

        Assert.True(verify.IsVerified);
        return verify.Ticket!;
    }

    private SignUpDonorCommand Command(
        string phone,
        string? ticket,
        string? email = null,
        string password = Password,
        IReadOnlyCollection<string>? acceptedHashes = null,
        string ipAddress = "203.0.113.9") =>
        new(LocalSpelling(phone), ticket, "Dana Donor", email, password, "si",
            acceptedHashes ?? BothDocuments, ipAddress, "xunit");

    private async Task<SignUpDonorResult> SignUpAsync(SignUpDonorCommand command)
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SignUpDonorHandler>().HandleAsync(command);
    }

    private async Task<int> CountUsersAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.Users WHERE PhoneNumber = @phone;", new { phone });
    }

    [Fact]
    public async Task WritesAllFiveTables_WithExactlyOneOutboxEvent()
    {
        var phone = NewPhone();
        var email = $"Dana.{Guid.NewGuid():N}@Example.lk";

        var result = await SignUpAsync(Command(phone, await VerifiedTicketAsync(phone), email));

        Assert.True(result.Succeeded, result.ToString());
        var userId = result.UserId!.Value;
        Assert.Equal(7, userId.Version); // plan A9

        await using var connection = new SqlConnection(database.ConnectionString);

        var user = await connection.QuerySingleAsync<(string PhoneNumber, bool PhoneConfirmed, string NormalizedEmail, string UserType, string Status, string PreferredLanguage, string PasswordHash, int PermissionsVersion)>(
            """
            SELECT PhoneNumber, PhoneConfirmed, NormalizedEmail, UserType, Status, PreferredLanguage, PasswordHash, PermissionsVersion
            FROM auth.Users WHERE Id = @userId;
            """,
            new { userId });

        Assert.Equal(phone, user.PhoneNumber);                              // stored E.164, typed 07...
        Assert.True(user.PhoneConfirmed);
        Assert.Equal(email.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Equal("Donor", user.UserType);
        Assert.Equal("Active", user.Status);
        Assert.Equal("si", user.PreferredLanguage);
        Assert.DoesNotContain(Password, user.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(1, user.PermissionsVersion);

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.DonorProfiles WHERE UserId = @userId;", new { userId }));

        var role = await connection.QuerySingleAsync<(string Code, int? InstitutionId)>(
            """
            SELECT r.Code, ur.InstitutionId FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @userId AND ur.IsActive = 1;
            """,
            new { userId });
        Assert.Equal(("DONOR", (int?)null), role);

        var consented = await connection.QueryAsync<string>(
            """
            SELECT d.Code FROM auth.UserConsents c JOIN core.LegalDocuments d ON d.Id = c.LegalDocumentId
            WHERE c.UserId = @userId ORDER BY d.Code;
            """,
            new { userId });
        Assert.Equal(["PRIVACY", "TERMS"], consented);

        var outbox = (await connection.QueryAsync<(string EventType, string Payload)>(
            "SELECT EventType, Payload FROM core.OutboxMessages WHERE AggregateType = 'User' AND AggregateId = @id;",
            new { id = userId.ToString() })).ToList();

        var message = Assert.Single(outbox);
        Assert.Equal("DonorSignedUp", message.EventType);
        Assert.DoesNotContain(phone[1..], message.Payload, StringComparison.Ordinal); // no PII in the outbox
    }

    [Fact]
    public async Task TheNewAccountCanSignInAtOnce()
    {
        var phone = NewPhone();
        Assert.True((await SignUpAsync(Command(phone, await VerifiedTicketAsync(phone)))).Succeeded);

        using var scope = _host.Services.CreateScope();
        var signIn = await scope.ServiceProvider.GetRequiredService<SignInHandler>()
            .HandleAsync(new SignInCommand(LocalSpelling(phone), Password, "203.0.113.9", "xunit"));

        Assert.True(signIn.Succeeded);
    }

    [Fact]
    public async Task RefusesAPhoneThatWasNotVerified()
    {
        var phone = NewPhone();

        var result = await SignUpAsync(Command(phone, ticket: "not-a-ticket"));

        Assert.Equal(SignUpFailure.PhoneNotVerified, result.Failure);
        Assert.Equal(0, await CountUsersAsync(phone));
    }

    [Fact]
    public async Task RefusesALandline_EvenWithATicketForIt()
    {
        // The endpoint never texts a landline (QA DEF-002); the service is asked directly here, so
        // this is the handler's own check: an account must be able to receive its sign-in codes.
        var landline = "+94112" + RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var ticket = await VerifiedTicketAsync(landline);

        var result = await SignUpAsync(Command(landline, ticket));

        Assert.Equal(SignUpFailure.InvalidPhoneNumber, result.Failure);
        Assert.Equal(0, await CountUsersAsync(landline));
    }

    [Fact]
    public async Task ATicketCreatesOneAccountOnly()
    {
        var phone = NewPhone();
        var ticket = await VerifiedTicketAsync(phone);

        Assert.True((await SignUpAsync(Command(phone, ticket))).Succeeded);

        var second = await SignUpAsync(Command(phone, ticket, email: $"other.{Guid.NewGuid():N}@example.lk"));

        Assert.Equal(SignUpFailure.PhoneNotVerified, second.Failure);
        Assert.Equal(1, await CountUsersAsync(phone));
    }

    [Fact]
    public async Task MistakesInTheFormDoNotSpendTheTicket()
    {
        // A typo should cost the donor a correction, not another SMS - and us another SMS charge.
        var phone = NewPhone();
        var ticket = await VerifiedTicketAsync(phone);

        var personalPassword = await SignUpAsync(Command(phone, ticket, password: "Dana!" + phone[3..]));
        Assert.Equal(SignUpFailure.PasswordRejected, personalPassword.Failure);
        Assert.True(personalPassword.PasswordFailures.HasFlag(PasswordFailure.ContainsPersonalData));

        Assert.Equal(SignUpFailure.InvalidEmail, (await SignUpAsync(Command(phone, ticket, email: "Dana <dana@example.lk>"))).Failure);
        Assert.Equal(SignUpFailure.ConsentMissing, (await SignUpAsync(Command(phone, ticket, acceptedHashes: [_terms]))).Failure);

        Assert.True((await SignUpAsync(Command(phone, ticket))).Succeeded);
    }

    [Fact]
    public async Task AnEmailDifferingOnlyInCaseIsTheSameAccount()
    {
        // Plan 5.8. Mutation testing showed what actually carries this: the column's case-insensitive
        // collation (SQL_Latin1_General_CP1_CI_AS). With NormalizedEmail left un-normalised, the
        // lookup and UX_Users_NormalizedEmail still treat these as one address, and this test stays
        // green. The normaliser keeps the stored value canonical for anything comparing outside SQL
        // Server; WritesAllFiveTables is the test that pins it.
        var stem = Guid.NewGuid().ToString("N");
        var first = NewPhone();
        var second = NewPhone();

        Assert.True((await SignUpAsync(Command(first, await VerifiedTicketAsync(first), email: $"Test.{stem}@X.lk"))).Succeeded);

        var duplicate = await SignUpAsync(Command(second, await VerifiedTicketAsync(second), email: $"test.{stem}@x.LK"));

        Assert.Equal(SignUpFailure.AlreadyRegistered, duplicate.Failure);
        Assert.Equal(0, await CountUsersAsync(second));
    }

    [Fact]
    public async Task SaysAPhoneIsRegisteredOnlyToWhoeverVerifiedIt()
    {
        var phone = NewPhone();
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PreferredLanguage, Status)
                VALUES (NEWID(), 'Donor', N'Existing Donor', @phone, 'en', 'Active');
                """,
                new { phone });
        }

        // Without a ticket the answer is the same as for any unverified number, so the signup form
        // cannot be used to find out which numbers have accounts.
        Assert.Equal(SignUpFailure.PhoneNotVerified, (await SignUpAsync(Command(phone, "not-a-ticket"))).Failure);

        Assert.Equal(SignUpFailure.AlreadyRegistered, (await SignUpAsync(Command(phone, await VerifiedTicketAsync(phone)))).Failure);
        Assert.Equal(1, await CountUsersAsync(phone));
    }

    [Fact]
    public async Task ConsentMustBeToTheCurrentTextOfBothDocuments()
    {
        var phone = NewPhone();
        var madeUp = Convert.ToHexStringLower(SHA256.HashData("not the terms anyone was shown"u8));

        Assert.Equal(SignUpFailure.ConsentMissing, (await SignUpAsync(Command(phone, "unused", acceptedHashes: []))).Failure);
        Assert.Equal(SignUpFailure.ConsentMissing, (await SignUpAsync(Command(phone, "unused", acceptedHashes: [_terms]))).Failure);
        Assert.Equal(SignUpFailure.ConsentMissing, (await SignUpAsync(Command(phone, "unused", acceptedHashes: [_privacy]))).Failure);
        Assert.Equal(SignUpFailure.ConsentMissing, (await SignUpAsync(Command(phone, "unused", acceptedHashes: [_terms, madeUp]))).Failure);
    }

    [Fact]
    public async Task RollsBackEveryTableWhenAnyInsertFails()
    {
        // Plan 5.8. A CHECK that refuses one IP address makes the consent insert - which follows the
        // user's by FK - fail at run time, after the user row has already been written inside the
        // transaction. If the five inserts were not one transaction, that row would survive.
        const string poisonIp = "198.51.100.66";
        var phone = NewPhone();
        var ticket = await VerifiedTicketAsync(phone);

        await using var connection = new SqlConnection(database.ConnectionString);
        const string counts =
            """
            SELECT (SELECT COUNT(*) FROM auth.Users) + (SELECT COUNT(*) FROM auth.DonorProfiles)
                 + (SELECT COUNT(*) FROM auth.UserRoles) + (SELECT COUNT(*) FROM auth.UserConsents)
                 + (SELECT COUNT(*) FROM core.OutboxMessages);
            """;
        var before = await connection.ExecuteScalarAsync<int>(counts);

        await connection.ExecuteAsync(
            $"ALTER TABLE auth.UserConsents ADD CONSTRAINT CK_Test_PoisonConsent CHECK (IpAddress <> '{poisonIp}');");
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => SignUpAsync(Command(phone, ticket, ipAddress: poisonIp)));
        }
        finally
        {
            await connection.ExecuteAsync("ALTER TABLE auth.UserConsents DROP CONSTRAINT CK_Test_PoisonConsent;");
        }

        Assert.Equal(0, await CountUsersAsync(phone));
        Assert.Equal(before, await connection.ExecuteScalarAsync<int>(counts));
    }
}
