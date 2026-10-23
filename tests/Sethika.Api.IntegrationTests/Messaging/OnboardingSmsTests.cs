using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Onboarding.Applying;
using Sethika.Application.Onboarding.Approval;
using Sethika.Application.Onboarding.Notifications;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Messaging;

/// <summary>
/// Plan 02, 5.7: the four onboarding texts, end to end below HTTP - outbox row, processor,
/// <see cref="OnboardingSmsHandler"/>, the seeded templates, <see cref="UserTextMessages"/> and the
/// console sender writing <c>notify.Notifications</c>.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class OnboardingSmsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private readonly List<Guid> _users = [];

    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;");
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            DELETE FROM notify.Notifications WHERE DedupKey LIKE 'ONBOARD[_]%:REQ-TEST-SMS-%';
            DELETE FROM core.OutboxMessages WHERE AggregateId LIKE 'REQ-TEST-SMS-%';
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { users = _users });
    }

    // ------------------------------------------------------------------ helpers

    private OutboxProcessor Processor()
    {
        var connections = new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform());
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var texts = new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance);

        return new OutboxProcessor(
            connections,
            [new OnboardingSmsHandler(texts)],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);
    }

    private async Task<(Guid Id, string Phone)> ApplicantAsync(string language, bool confirmed = true)
    {
        var id = Guid.CreateVersion7();
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status)
            VALUES (@id, 'Donor', N'Applicant', @phone, @confirmed, @language, 'Active');
            """,
            new { id, phone, confirmed, language });
        return (id, phone);
    }

    private async Task<long> EventAsync(string eventType, string requestNo, Guid applicant, string? payload = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT core.OutboxMessages (EventType, AggregateType, AggregateId, Payload)
            OUTPUT inserted.Id
            VALUES (@eventType, 'RegistrationRequest', @requestNo, @payload);
            """,
            new { eventType, requestNo, payload = payload ?? JsonSerializer.Serialize(new { requestNo, applicantUserId = applicant }) });
    }

    private sealed record Sent(string Recipient, string Body, string DedupKey, string Status);

    private async Task<List<Sent>> SentToAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<Sent>(
            "SELECT Recipient AS Recipient, Body AS Body, DedupKey AS DedupKey, Status AS Status FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;",
            new { phone })];
    }

    private async Task<string> TemplateAsync(string code, string language)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM notify.Templates WHERE Code = @code AND Channel = 'SMS' AND Language = @language;",
            new { code, language }) ?? throw new InvalidOperationException($"{code} {language} is not seeded.");
    }

    private async Task<(bool Processed, byte Attempts, string? LastError)> StateOfAsync(long id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<(bool, byte, string?)>(
            "SELECT CAST(IIF(ProcessedAt IS NULL, 0, 1) AS BIT), Attempts, LastError FROM core.OutboxMessages WHERE Id = @id;",
            new { id });
    }

    // ------------------------------------------------------------------ tests

    public static TheoryData<string, string> EventsAndTemplates() => new()
    {
        { ApplicationHandler.SubmittedEvent, "ONBOARD_SUBMITTED" },
        { OnboardingSmsHandler.InfoRequestedEvent, "ONBOARD_INFO_REQUESTED" },
        { ApproveRegistrationHandler.ApprovedEvent, "ONBOARD_APPROVED" },
        { OnboardingSmsHandler.RejectedEvent, "ONBOARD_REJECTED" },
    };

    [Theory]
    [MemberData(nameof(EventsAndTemplates))]
    public async Task EachEventTextsTheApplicantItsTemplate_InTheirLanguage(string eventType, string template)
    {
        var (applicant, phone) = await ApplicantAsync("si");
        var id = await EventAsync(eventType, "REQ-TEST-SMS-0001", applicant);

        await Processor().RunAsync();

        var sent = Assert.Single(await SentToAsync(phone));
        Assert.Equal(
            new Sent(phone, (await TemplateAsync(template, "si")).Replace("{requestNo}", "REQ-TEST-SMS-0001", StringComparison.Ordinal), $"{template}:REQ-TEST-SMS-0001:{id}", "Sent"),
            sent);
        Assert.Equal((true, (byte)0, (string?)null), await StateOfAsync(id));
    }

    [Fact]
    public async Task ARunRepeatedAfterTheSendTextsNobodyTwice()
    {
        // As if the process died after the SMS and before the commit: the message comes round again.
        var (applicant, phone) = await ApplicantAsync("en");
        var id = await EventAsync(ApproveRegistrationHandler.ApprovedEvent, "REQ-TEST-SMS-0002", applicant);
        await Processor().RunAsync();

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = NULL WHERE Id = @id;", new { id });
        }

        await Processor().RunAsync();

        Assert.Single(await SentToAsync(phone));
        Assert.True((await StateOfAsync(id)).Processed);
    }

    [Fact]
    public async Task EveryQuestionIsItsOwnText()
    {
        var (applicant, phone) = await ApplicantAsync("ta");
        await EventAsync(OnboardingSmsHandler.InfoRequestedEvent, "REQ-TEST-SMS-0003", applicant);
        await EventAsync(OnboardingSmsHandler.InfoRequestedEvent, "REQ-TEST-SMS-0003", applicant);

        await Processor().RunAsync();

        Assert.Equal(2, (await SentToAsync(phone)).Count);
    }

    [Fact]
    public async Task AnUnconfirmedPhoneIsNotTexted()
    {
        var (applicant, phone) = await ApplicantAsync("en", confirmed: false);
        var id = await EventAsync(ApplicationHandler.SubmittedEvent, "REQ-TEST-SMS-0004", applicant);

        await Processor().RunAsync();

        Assert.Empty(await SentToAsync(phone));
        Assert.True((await StateOfAsync(id)).Processed);
    }

    [Fact]
    public async Task ALanguageWithoutTheTemplateGetsEnglish_AndNoTemplateAtAllIsAFailure()
    {
        var (tamil, tamilPhone) = await ApplicantAsync("ta");
        var (sinhala, sinhalaPhone) = await ApplicantAsync("si");

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            UPDATE notify.Templates SET IsActive = 0 WHERE Code = 'ONBOARD_REJECTED' AND Language = 'ta';
            UPDATE notify.Templates SET IsActive = 0 WHERE Code = 'ONBOARD_APPROVED';
            """);
        try
        {
            await EventAsync(OnboardingSmsHandler.RejectedEvent, "REQ-TEST-SMS-0005", tamil);
            var approved = await EventAsync(ApproveRegistrationHandler.ApprovedEvent, "REQ-TEST-SMS-0006", sinhala);

            await Processor().RunAsync();

            Assert.Equal(
                (await TemplateAsync("ONBOARD_REJECTED", "en")).Replace("{requestNo}", "REQ-TEST-SMS-0005", StringComparison.Ordinal),
                Assert.Single(await SentToAsync(tamilPhone)).Body);

            Assert.Empty(await SentToAsync(sinhalaPhone));
            var (processed, attempts, error) = await StateOfAsync(approved);
            Assert.Equal((false, (byte)1), (processed, attempts));
            Assert.Contains("ONBOARD_APPROVED", error, StringComparison.Ordinal);
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE notify.Templates SET IsActive = 1 WHERE Code IN ('ONBOARD_REJECTED', 'ONBOARD_APPROVED');");
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"requestNo":"REQ-TEST-SMS-0007"}""")]
    [InlineData("""{"applicantUserId":"0190a1d2-0000-7000-8000-000000000001"}""")]
    public async Task APayloadWithoutTheRequestOrTheApplicantIsAFailure(string payload)
    {
        var id = await EventAsync(ApplicationHandler.SubmittedEvent, "REQ-TEST-SMS-0007", Guid.Empty, payload);

        await Processor().RunAsync();

        var (processed, attempts, _) = await StateOfAsync(id);
        Assert.Equal((false, (byte)1), (processed, attempts));
    }
}
