using Microsoft.EntityFrameworkCore;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Core;
using Sethika.Domain.Onboarding;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Api.IntegrationTests.Onboarding;

/// <summary>
/// Builds applications in the real database the way the flow would: the applicant fills in and
/// submits as themselves, the reviewer reviews as the platform. Alpha applies, Beta reviews.
/// </summary>
internal sealed class OnboardingScenario(SethikaTestDatabase database)
{
    public static readonly Guid Applicant = SethikaTestDatabase.AlphaUserId;
    public static readonly Guid Reviewer = SethikaTestDatabase.BetaUserId;
    public static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    public const string ApplicantNic = "199012345678";
    public const string ApplicantPhone = "+94771000001";

    public SethikaDbContext ContextFor(TestCurrentUserContext user) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options);

    public SethikaDbContext AsApplicant() => ContextFor(TestCurrentUserContext.ForUser(Applicant));

    public SethikaDbContext AsPlatform(Guid userId) => ContextFor(new TestCurrentUserContext { UserId = userId, IsPlatform = true });

    public SethikaDbContext AsReviewer() => AsPlatform(Reviewer);

    /// <summary>
    /// An application under review with every mandatory document verified and every checklist item
    /// passed: one step from approval.
    /// </summary>
    public async Task ReadyToApproveAsync(string requestNo, string registrationNo, string name = "Sri Sudharmaramaya", string city = "Galle")
    {
        short[] mandatory;

        await using (var applicant = AsApplicant())
        {
            var typeId = await applicant.Database.SqlQuery<short>($"SELECT Id AS Value FROM org.InstitutionTypes WHERE Code = 'TEMPLE'").SingleAsync();
            var districtId = await applicant.Database.SqlQuery<short>($"SELECT Id AS Value FROM core.Districts WHERE Code = 'GLL'").SingleAsync();
            mandatory = await applicant.InstitutionTypeRequiredDocuments
                .Where(d => d.InstitutionTypeId == typeId && d.IsMandatory)
                .Select(d => d.DocumentTypeId)
                .ToArrayAsync();

            var request = RegistrationRequest.StartDraft(requestNo, Applicant, Now);
            request.InstitutionTypeId = typeId;
            request.ProposedName = name;
            request.OfficialRegistrationNo = registrationNo;
            request.RegisteringAuthority = "Department of Buddhist Affairs";
            request.RegistrationDate = new DateOnly(1952, 4, 1);
            request.AddressLine1 = "12 Temple Road";
            request.City = city;
            request.DistrictId = districtId;
            request.Latitude = 6.053500m;
            request.Longitude = 80.221000m;
            request.OfficePhone = "+94912234567";
            request.ApplicantFullName = "Alpha Steward";
            request.ApplicantNic = ApplicantNic;
            request.ApplicantDesignation = "Secretary";
            request.ApplicantPhone = ApplicantPhone;

            request.ReplaceOfficeBearers(
                [new RegistrationOfficeBearer { FullName = "Chief Trustee", NicNumber = "901234567V", Designation = "Trustee", Phone = "+94771234567", IsSignatory = true }],
                Applicant,
                Now);

            foreach (var type in mandatory)
            {
                var file = new StoredFile
                {
                    PublicId = Guid.NewGuid(),
                    StorageProvider = "Local",
                    StorageKey = "2026/10/" + Guid.NewGuid().ToString("N"),
                    OriginalName = $"document-{type}.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 1_000,
                    Sha256Hash = new string('b', 64),
                    UploadedBy = Applicant,
                    UploadedAt = Now,
                };
                applicant.StoredFiles.Add(file);
                await applicant.SaveChangesAsync();

                request.AttachDocument(new RegistrationRequestDocument { DocumentTypeId = type, FileId = file.Id, ReferenceNo = $"REF-{type}" }, Applicant, Now);
            }

            Assert.True(request.Submit(mandatory, declarationDocumentId: 1, "203.0.113.5", Applicant, Now).Succeeded);
            applicant.RegistrationRequests.Add(request);
            await applicant.SaveChangesAsync();
        }

        await using var reviewer = AsReviewer();
        var submitted = await reviewer.RegistrationRequests
            .Include(r => r.Documents)
            .Include(r => r.ChecklistEntries)
            .Include(r => r.StatusHistory)
            .SingleAsync(r => r.RequestNo == requestNo);
        var checklist = await reviewer.VerificationChecklistItems.Where(i => i.IsActive).Select(i => i.Id).ToListAsync();

        Assert.True(submitted.StartReview(Reviewer, Now).Succeeded);
        foreach (var type in mandatory)
        {
            Assert.True(submitted.VerifyDocument(type, true, null, Reviewer, Now).Succeeded);
        }

        foreach (var item in checklist)
        {
            Assert.True(submitted.RecordChecklist(item, true, null, Reviewer, Now).Succeeded);
        }

        await reviewer.SaveChangesAsync();
    }
}
