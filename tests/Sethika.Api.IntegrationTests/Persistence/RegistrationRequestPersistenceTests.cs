using Dapper;
using Microsoft.EntityFrameworkCore;
using Sethika.Domain.Core;
using Sethika.Domain.Onboarding;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Plan 02, 5.2: the application aggregate saved and loaded through EF Core against the real
/// tables, under Row-Level Security - the applicant as themselves, the reviewer as the platform.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RegistrationRequestPersistenceTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private static readonly Guid Applicant = SethikaTestDatabase.AlphaUserId;
    private static readonly Guid Reviewer = SethikaTestDatabase.BetaUserId;
    private static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
    private const string RequestNo = "REQ-TEST-000001";

    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => database.SeedAsync();

    private SethikaDbContext ContextFor(TestCurrentUserContext user) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options);

    private SethikaDbContext AsApplicant() => ContextFor(TestCurrentUserContext.ForUser(Applicant));

    private SethikaDbContext AsReviewer() => ContextFor(new TestCurrentUserContext { UserId = Reviewer, IsPlatform = true });

    private static Task<RegistrationRequest> LoadAsync(SethikaDbContext context) =>
        context.RegistrationRequests
            .Include(r => r.Documents)
            .Include(r => r.OfficeBearers)
            .Include(r => r.ChecklistEntries)
            .Include(r => r.Clarifications)
            .Include(r => r.StatusHistory)
            .AsSplitQuery()
            .SingleAsync(r => r.RequestNo == RequestNo);

    private static async Task<short[]> MandatoryDocumentsAsync(SethikaDbContext context, short typeId) =>
        await context.InstitutionTypeRequiredDocuments
            .Where(d => d.InstitutionTypeId == typeId && d.IsMandatory)
            .Select(d => d.DocumentTypeId)
            .ToArrayAsync();

    private static StoredFile File(string name) => new()
    {
        PublicId = Guid.NewGuid(),
        StorageProvider = "Local",
        StorageKey = "2026/10/" + Guid.NewGuid().ToString("N"),
        OriginalName = name,
        ContentType = "application/pdf",
        SizeBytes = 1_000,
        Sha256Hash = new string('a', 64),
        UploadedBy = Applicant,
        UploadedAt = Now,
    };

    private static RegistrationOfficeBearer Bearer(string nic, string name, bool signatory = true) => new()
    {
        FullName = name,
        NicNumber = nic,
        Designation = "Trustee",
        Phone = "+94771234567",
        IsSignatory = signatory,
    };

    /// <summary>A draft with its details, saved by the applicant, with one upload per mandatory type.</summary>
    private async Task<short[]> SaveDraftAsync(bool withDocuments = true)
    {
        await using var context = AsApplicant();
        var typeId = await context.Database.SqlQuery<short>($"SELECT Id AS Value FROM org.InstitutionTypes WHERE Code = 'TEMPLE'").SingleAsync();
        var districtId = await context.Database.SqlQuery<short>($"SELECT Id AS Value FROM core.Districts WHERE Code = 'GLL'").SingleAsync();

        var request = RegistrationRequest.StartDraft(RequestNo, Applicant, Now);
        request.InstitutionTypeId = typeId;
        request.ProposedName = "Sri Sudharmaramaya";
        request.NameSi = "ශ්‍රී සුධර්මාරාමය";
        request.OfficialRegistrationNo = "BUD/GAL/1952/77";
        request.RegisteringAuthority = "Department of Buddhist Affairs";
        request.RegistrationDate = new DateOnly(1952, 4, 1);
        request.AddressLine1 = "12 Temple Road";
        request.City = "Galle";
        request.DistrictId = districtId;
        request.Latitude = 6.053500m;
        request.Longitude = 80.221000m;
        request.OfficePhone = "+94912234567";
        request.ApplicantFullName = "Alpha Steward";
        request.ApplicantNic = "199012345678";
        request.ApplicantDesignation = "Secretary";
        request.ApplicantPhone = "+94771000001";

        Assert.True(request.ReplaceOfficeBearers([Bearer("901234567V", "Chief Trustee")], Applicant, Now).Succeeded);

        var mandatory = await MandatoryDocumentsAsync(context, typeId);
        if (withDocuments)
        {
            foreach (var type in mandatory)
            {
                var file = File($"document-{type}.pdf");
                context.StoredFiles.Add(file);
                await context.SaveChangesAsync();
                Assert.True(request.AttachDocument(new RegistrationRequestDocument { DocumentTypeId = type, FileId = file.Id }, Applicant, Now).Succeeded);
            }
        }

        context.RegistrationRequests.Add(request);
        await context.SaveChangesAsync();
        return mandatory;
    }

    /// <summary>Reads as the platform: an unstamped connection would see no applications at all (RLS).</summary>
    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform())
            .OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, new { requestNo = RequestNo }) ?? throw new InvalidOperationException(sql);
    }

    // ------------------------------------------------------------------ the whole aggregate

    [Fact]
    public async Task AnApplicationGoesFromDraftToAnsweredQuestionsAndEveryRowIsWritten()
    {
        var mandatory = await SaveDraftAsync();

        await using (var applicant = AsApplicant())
        {
            var request = await LoadAsync(applicant);
            Assert.True(request.Submit(mandatory, declarationDocumentId: 1, "203.0.113.5", Applicant, Now).Succeeded);
            await applicant.SaveChangesAsync();
        }

        await using (var reviewer = AsReviewer())
        {
            var request = await LoadAsync(reviewer);
            var checklist = await reviewer.VerificationChecklistItems.Select(i => i.Id).ToListAsync();

            Assert.True(request.StartReview(Reviewer, Now).Succeeded);
            foreach (var type in mandatory)
            {
                Assert.True(request.VerifyDocument(type, true, null, Reviewer, Now).Succeeded);
            }

            foreach (var item in checklist)
            {
                Assert.True(request.RecordChecklist(item, true, null, Reviewer, Now).Succeeded);
            }

            Assert.True(request.RequestInformation(["Who signs for the society?"], Reviewer, Now).Succeeded);
            await reviewer.SaveChangesAsync();
        }

        await using (var applicant = AsApplicant())
        {
            var request = await LoadAsync(applicant);
            Assert.True(request.Answer(["The secretary, with the chief trustee"], Applicant, Now).Succeeded);
            await applicant.SaveChangesAsync();
        }

        await using var check = AsReviewer();
        var saved = await LoadAsync(check);

        Assert.Equal(RegistrationStatus.UnderReview, saved.Status);
        Assert.Equal(
            [RegistrationStatus.Draft, RegistrationStatus.Submitted, RegistrationStatus.UnderReview, RegistrationStatus.InfoRequested, RegistrationStatus.UnderReview],
            saved.StatusHistory.OrderBy(h => h.Id).Select(h => h.ToStatus));
        Assert.All(saved.LiveDocuments, d => Assert.Equal(DocumentVerification.Verified, d.VerificationStatus));
        Assert.Equal(4, saved.ChecklistEntries.Count(e => e.IsPassed));
        Assert.Equal("The secretary, with the chief trustee", Assert.Single(saved.Clarifications).Response);
        Assert.Equal("ශ්‍රී සුධර්මාරාමය", saved.NameSi);
        Assert.Equal(new DateOnly(1952, 4, 1), saved.RegistrationDate);
        Assert.Equal("203.0.113.5", saved.DeclarationIp);
    }

    // ------------------------------------------------------------------ the unique indexes EF must order around

    [Fact]
    public async Task ReplacingADocumentSupersedesTheOldOneInTheSameSave()
    {
        var mandatory = await SaveDraftAsync();

        await using (var applicant = AsApplicant())
        {
            var request = await LoadAsync(applicant);
            var file = File("clearer-scan.pdf");
            applicant.StoredFiles.Add(file);
            await applicant.SaveChangesAsync();

            Assert.True(request.AttachDocument(new RegistrationRequestDocument { DocumentTypeId = mandatory[0], FileId = file.Id }, Applicant, Now).Succeeded);
            await applicant.SaveChangesAsync();
        }

        Assert.Equal(1, await ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM org.RegistrationRequestDocuments d JOIN org.RegistrationRequests r ON r.Id = d.RequestId
            WHERE r.RequestNo = @requestNo AND d.DocumentTypeId = {mandatory[0]} AND d.VerificationStatus = 'Superseded';
            """));
    }

    [Fact]
    public async Task ReplacingTheOfficeBearersCanNameTheSamePersonAgain()
    {
        await SaveDraftAsync(withDocuments: false);

        await using (var applicant = AsApplicant())
        {
            var request = await LoadAsync(applicant);
            Assert.True(request.ReplaceOfficeBearers(
                [Bearer("901234567V", "Chief Trustee (renamed)"), Bearer("801234567V", "Treasurer", signatory: false)],
                Applicant,
                Now).Succeeded);
            await applicant.SaveChangesAsync();
        }

        Assert.Equal(2, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM org.RegistrationRequestOfficeBearers b JOIN org.RegistrationRequests r ON r.Id = b.RequestId
            WHERE r.RequestNo = @requestNo;
            """));
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task TwoReviewersVerifyingAtOnceCannotBothSave()
    {
        // Each changes a different document row; only the application row's RowVersion connects them.
        var mandatory = await SaveDraftAsync();
        await using (var setup = AsReviewer())
        {
            var request = await LoadAsync(setup);
            request.Submit(mandatory, 1, null, Applicant, Now);
            request.StartReview(Reviewer, Now);
            await setup.SaveChangesAsync();
        }

        await using var first = AsReviewer();
        await using var second = AsReviewer();
        var mine = await LoadAsync(first);
        var theirs = await LoadAsync(second);

        Assert.True(mine.VerifyDocument(mandatory[0], true, null, Reviewer, Now).Succeeded);
        await first.SaveChangesAsync();

        Assert.True(theirs.VerifyDocument(mandatory[1], false, "Unreadable", Reviewer, Now).Succeeded);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
