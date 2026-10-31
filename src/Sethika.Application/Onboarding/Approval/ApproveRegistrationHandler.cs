using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;
using Sethika.Domain.Institutions;
using Sethika.Domain.Onboarding;

namespace Sethika.Application.Onboarding.Approval;

/// <summary>Approve an application, as the platform user <paramref name="DeciderId"/>.</summary>
/// <param name="ClientIp">Where the decision came from (plan 01, A18), for the audit log.</param>
public sealed record ApproveRegistrationCommand(string RequestNo, Guid DeciderId, string? ClientIp);

/// <summary>The institution an approval created, or why there is none.</summary>
public sealed record ApproveRegistrationResult(string? Error, string? InstitutionCode, string? Slug)
{
    /// <summary>No application with that number - or none this caller may see.</summary>
    public const string NotFound = "registration_not_found";

    public bool Succeeded => Error is null;

    internal static ApproveRegistrationResult Failed(string error) => new(error, null, null);
}

/// <summary>
/// Turns an application into an institution, in one transaction (plan 02, B6).
/// </summary>
/// <remarks>
/// Everything an approval creates is written together or not at all: the institution, its
/// documents, its modules, its roles copied from the templates, the applicant as owner, both
/// status histories, the audit row and the outbox message. A failure anywhere rolls back every
/// one of them - the institution number included, so the series stays gap-free.
///
/// The decision locks (<see cref="IRegistrationLocks"/>) are taken first, before anything is read,
/// so two approvals never race for the same application, slug or owner.
/// </remarks>
public sealed class ApproveRegistrationHandler(
    ISethikaDbContext db,
    IRegistrationLocks locks,
    INumberSeries numbers,
    IInstitutionModules modules,
    TimeProvider timeProvider)
{
    public const string ApprovedEvent = "InstitutionApproved";

    private const string InstitutionScope = "Institution";
    private const string ActiveStatus = "Active";
    private const string OwnerRoleCode = "OWNER";

    public async Task<ApproveRegistrationResult> HandleAsync(
        ApproveRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestNo);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (!await locks.ClaimForDecisionAsync(command.RequestNo, cancellationToken).ConfigureAwait(false))
        {
            return ApproveRegistrationResult.Failed(ApproveRegistrationResult.NotFound);
        }

        // 1. The application, and what its type requires.
        var request = await db.LoadRegistrationAsync(command.RequestNo, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{command.RequestNo} was claimed and then could not be read.");

        var mandatoryDocuments = await db.InstitutionTypeRequiredDocuments
            .Where(d => d.InstitutionTypeId == request.InstitutionTypeId && d.IsMandatory)
            .Select(d => d.DocumentTypeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mandatoryChecklist = await db.VerificationChecklistItems
            .Where(i => i.IsActive && i.IsMandatory && (i.InstitutionTypeId == null || i.InstitutionTypeId == request.InstitutionTypeId))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ready = request.CheckReadyForApproval(mandatoryDocuments, mandatoryChecklist, command.DeciderId);
        if (!ready.Succeeded)
        {
            return ApproveRegistrationResult.Failed(ready.Error!);
        }

        // 2-3. The institution, with its number and its slug.
        var code = await numbers
            .NextAsync(NumberSeriesDefinition.Institution, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);
        var slug = await FreeSlugAsync(InstitutionSlug.From(request.ProposedName, request.City) ?? code.ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

        var institution = NewInstitution(request, code, slug, command.DeciderId, now);
        db.Institutions.Add(institution);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 4. Its documents: the verified uploads, pointing at the same files.
        foreach (var document in request.LiveDocuments.Where(d => d.VerificationStatus == DocumentVerification.Verified))
        {
            db.InstitutionDocuments.Add(new InstitutionDocument
            {
                InstitutionId = institution.Id,
                DocumentTypeId = document.DocumentTypeId,
                FileId = document.FileId,
                SourceRequestDocumentId = document.Id,
                ReferenceNo = document.ReferenceNo,
                IssuedDate = document.IssuedDate,
                ExpiryDate = document.ExpiryDate,
                CreatedAt = now,
            });
        }

        // 5. Its modules.
        await modules
            .EnableForNewInstitutionAsync(institution.Id, request.InstitutionTypeId, request.RequestedPlanId, command.DeciderId, $"Approved {request.RequestNo}", cancellationToken)
            .ConfigureAwait(false);

        // 6. Its roles, copied from the templates with what they grant.
        var owner = await CloneTemplateRolesAsync(institution.Id, request.InstitutionTypeId, command.DeciderId, cancellationToken)
            .ConfigureAwait(false);

        // 7. The applicant as its owner. Their permissions changed, so their cached set must go.
        db.InstitutionMembers.Add(new InstitutionMember
        {
            InstitutionId = institution.Id,
            UserId = request.ApplicantUserId,
            Designation = request.ApplicantDesignation,
            IsOwner = true,
            JoinedAt = now,
            AddedBy = command.DeciderId,
        });

        db.UserRoles.Add(new UserRole
        {
            UserId = request.ApplicantUserId,
            RoleId = owner.Id,
            InstitutionId = institution.Id,
            AssignedBy = command.DeciderId,
            IsActive = true,
        });

        await db.Users
            .Where(u => u.Id == request.ApplicantUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionsVersion, u => u.PermissionsVersion + 1), cancellationToken)
            .ConfigureAwait(false);

        // 8. The application approved, and both histories.
        var approved = request.Approve(institution.Id, mandatoryDocuments, mandatoryChecklist, command.DeciderId, now);
        if (!approved.Succeeded)
        {
            // Checked above under the same locks; reaching this means the two checks disagree.
            throw new InvalidOperationException($"{request.RequestNo} was ready to approve and then was not: {approved.Error}.");
        }

        db.InstitutionStatusHistory.Add(new InstitutionStatusChange
        {
            InstitutionId = institution.Id,
            FromStatus = null,
            ToStatus = ActiveStatus,
            Reason = $"Approved {request.RequestNo}",
            ChangedBy = command.DeciderId,
            ChangedAt = now,
        });

        // 9. The audit row and the outbox message. Ids and codes only - no names, phones or NICs.
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = command.DeciderId,
            InstitutionId = institution.Id,
            Action = "Approve",
            EntityName = "RegistrationRequest",
            EntityId = request.RequestNo,
            OldValues = JsonSerializer.Serialize(new { status = RegistrationStatus.UnderReview }),
            NewValues = JsonSerializer.Serialize(new { status = RegistrationStatus.Approved, institutionCode = code, slug }),
            IpAddress = command.ClientIp,
            OccurredAt = now,
        });

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = ApprovedEvent,
            AggregateType = "RegistrationRequest",
            AggregateId = request.RequestNo,
            Payload = JsonSerializer.Serialize(new
            {
                requestNo = request.RequestNo,
                institutionCode = code,
                slug,
                applicantUserId = request.ApplicantUserId,
                by = command.DeciderId,
            }),
            OccurredAt = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ApproveRegistrationResult(null, code, slug);
    }

    private static Institution NewInstitution(RegistrationRequest request, string code, string slug, Guid deciderId, DateTime now) => new()
    {
        InstitutionCode = code,
        Slug = slug,
        RegistrationRequestId = request.Id,
        InstitutionTypeId = request.InstitutionTypeId,
        Name = request.ProposedName,
        NameSi = request.NameSi,
        NameTa = request.NameTa,
        OfficialRegistrationNo = request.OfficialRegistrationNo,
        Description = request.Description,
        AddressLine1 = request.AddressLine1,
        AddressLine2 = request.AddressLine2,
        City = request.City,
        DistrictId = request.DistrictId,
        DsDivisionId = request.DsDivisionId,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        Phone = request.OfficePhone,
        Email = request.OfficeEmail,
        ResidentCapacity = request.ResidentCapacity,

        // B7: live at once, but not listed publicly until Phase B's public pages exist.
        Status = ActiveStatus,
        IsPubliclyListed = false,
        VerifiedBadgeLevel = 1,
        PermissionsVersion = 1,
        ApprovedBy = deciderId,
        ApprovedAt = now,
        CreatedAt = now,
    };

    /// <summary>The slug, or the first free <c>-2</c>, <c>-3</c>, ...; safe because decisions are serialised.</summary>
    private async Task<string> FreeSlugAsync(string slug, CancellationToken cancellationToken)
    {
        var prefix = slug + "-";
        var taken = await db.Institutions
            .Where(i => i.Slug == slug || i.Slug.StartsWith(prefix))
            .Select(i => i.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return InstitutionSlug.FirstFree(slug, taken);
    }

    /// <summary>One institution role per active template for the type, granting what the template grants.</summary>
    /// <returns>The institution's owner role.</returns>
    private async Task<Role> CloneTemplateRolesAsync(int institutionId, short institutionTypeId, Guid deciderId, CancellationToken cancellationToken)
    {
        var templates = await db.Roles
            .AsNoTracking()
            .Where(r => r.IsTemplate && r.InstitutionId == null && r.IsActive && (r.InstitutionTypeId == null || r.InstitutionTypeId == institutionTypeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var templateIds = templates.Select(t => t.Id).ToList();
        var grants = await db.RolePermissions
            .AsNoTracking()
            .Where(rp => templateIds.Contains(rp.RoleId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var clones = templates.ToDictionary(t => t.Id, t => new Role
        {
            InstitutionId = institutionId,
            Scope = InstitutionScope,
            RoleLevelId = t.RoleLevelId,
            Code = t.Code,
            Name = t.Name,
            Description = t.Description,
            TemplateRoleId = t.Id,
            IsActive = true,
            CreatedBy = deciderId,
        });

        db.Roles.AddRange(clones.Values);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var grant in grants)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = clones[grant.RoleId].Id, PermissionId = grant.PermissionId, GrantedBy = deciderId });
        }

        return clones.Values.SingleOrDefault(r => r.Code == OwnerRoleCode)
            ?? throw new InvalidOperationException(
                "There is no active OWNER template role, so an approved institution would have no owner. " +
                "10_RbacCatalogue.sql seeds it.");
    }
}
