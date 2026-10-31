using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;

namespace Sethika.Application.Access.Platform;

/// <summary>Why a platform administration change was refused. Each name is the API's code in snake case.</summary>
public enum PlatformChangeFailure
{
    None = 0,
    InvalidFullName,
    InvalidPhoneNumber,
    InvalidLanguage,
    AlreadyRegistered,
    UserNotFound,
    RoleNotFound,
    InstitutionNotFound,
    OwnAccount,
    RankNotJunior,
    Unavailable,
}

/// <summary>The outcome of a platform administration change, with the new account's id after a create.</summary>
public sealed record PlatformChangeResult(PlatformChangeFailure Failure, Guid? Created = null)
{
    public bool Succeeded => Failure == PlatformChangeFailure.None;

    public static PlatformChangeResult Success { get; } = new(PlatformChangeFailure.None);

    public static PlatformChangeResult Failed(PlatformChangeFailure failure) => new(failure);
}

/// <summary>
/// The platform's own changes (plan 04, Y1a, Y5-Y6): a ticket into an institution as support,
/// accounts made for people who cannot sign up, suspending and reactivating accounts, and platform
/// roles. Each endpoint's [HasPermission] decides who may call; the rank rule is checked here, in
/// the transaction, as the institution role rules are (plan 01, 5.9).
/// </summary>
/// <remarks>
/// Rank among platform roles works as among institution roles: an administrator manages only roles
/// strictly junior to their own most senior one, and never their own account. So the first
/// administrator (bootstrap) can make reviewers, and nobody can make another administrator or
/// suspend one from here.
/// </remarks>
public sealed class PlatformAdminHandler(
    ISethikaDbContext db,
    ISupportTickets tickets,
    IPhoneNumberNormalizer phoneNumbers,
    TimeProvider timeProvider)
{
    private const string PlatformScope = "Platform";
    private const string PlatformStaff = "PlatformStaff";
    private const string DonorRoleCode = "DONOR";
    private const string Active = "Active";
    private const string PendingVerification = "PendingVerification";
    private const string Suspended = "Suspended";
    private const string ChangedEvent = "PlatformAdministration";
    private static readonly string[] Languages = ["en", "si", "ta"];

    /// <summary>A one-use ticket into a live institution (Y1a); the step-up was asked for by the endpoint's permission.</summary>
    public async Task<PlatformChangeResult> IssueSupportTicketAsync(Guid actor, string slug, CancellationToken cancellationToken = default)
    {
        var institution = await db.Institutions
            .AsNoTracking()
            .Where(i => i.Slug == slug && !i.IsDeleted && (i.Status == "Active" || i.Status == "Onboarding"))
            .Select(i => (int?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (institution is not { } id)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.InstitutionNotFound);
        }

        if (!await tickets.IssueAsync(actor, id, cancellationToken).ConfigureAwait(false))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.Unavailable);
        }

        Record(actor, "SupportTicketIssued", targetUserId: null, institutionId: id);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PlatformChangeResult.Success;
    }

    /// <summary>
    /// An account for someone who cannot sign up themselves (Y6): pending until they sign in with a
    /// texted code, which proves the number; no password; the DONOR role, as at signup.
    /// </summary>
    public async Task<PlatformChangeResult> CreateUserAsync(
        Guid actor,
        string? fullName,
        string? phone,
        string? language,
        CancellationToken cancellationToken = default)
    {
        var name = fullName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 150)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.InvalidFullName);
        }

        if (!phoneNumbers.TryNormalize(phone, out var normalized))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.InvalidPhoneNumber);
        }

        if (language is null || !Languages.Contains(language, StringComparer.Ordinal))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.InvalidLanguage);
        }

        if (await db.Users.AnyAsync(u => u.PhoneNumber == normalized, cancellationToken).ConfigureAwait(false))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.AlreadyRegistered);
        }

        var donorRoleId = await db.Roles
            .Where(r => r.InstitutionId == null && r.Code == DonorRoleCode && r.IsActive)
            .Select(r => r.Id)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Id = Guid.CreateVersion7(now),
            UserType = "Donor",
            FullName = name,
            PhoneNumber = normalized,
            PhoneConfirmed = false,
            PasswordHash = null,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            PermissionsVersion = 1,
            PreferredLanguage = language,
            Status = PendingVerification,
            CreatedAt = now.UtcDateTime,
            CreatedBy = actor,
        };

        db.Users.Add(user);
        db.DonorProfiles.Add(new DonorProfile { UserId = user.Id, CountryCode = "LK" });
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = donorRoleId, AssignedBy = actor, IsActive = true });
        Record(actor, "UserCreated", user.Id, institutionId: null);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            // The same number signed up, or was added by someone else, a moment ago.
            return PlatformChangeResult.Failed(PlatformChangeFailure.AlreadyRegistered);
        }

        return new PlatformChangeResult(PlatformChangeFailure.None, user.Id);
    }

    /// <summary>
    /// Suspends an account, or lets it back in. Suspending also rotates its security stamp, so every
    /// session it has ends at its next refresh (SessionHandler refuses a changed stamp).
    /// </summary>
    public async Task<PlatformChangeResult> SetSuspendedAsync(Guid actor, Guid userId, bool suspend, CancellationToken cancellationToken = default)
    {
        if (userId == actor)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.OwnAccount);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.UserNotFound);
        }

        // Nobody suspends someone holding a platform role at their own level or above.
        var targetRank = await PlatformRankAsync(userId, cancellationToken).ConfigureAwait(false);
        if (targetRank is not null && !await OutranksAsync(actor, targetRank.Value, cancellationToken).ConfigureAwait(false))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.RankNotJunior);
        }

        if (suspend)
        {
            user.Status = Suspended;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        else
        {
            user.Status = user.PhoneConfirmed || user.PasswordHash is not null ? Active : PendingVerification;
        }

        // The view grants nothing to an account that is not Active; a new version makes every
        // cached permission set miss, so a live access token is refused on its next request.
        user.PermissionsVersion++;

        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        user.UpdatedBy = actor;
        Record(actor, suspend ? "UserSuspended" : "UserReactivated", userId, institutionId: null);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return PlatformChangeResult.Success;
    }

    /// <summary>
    /// Gives a platform role strictly junior to the actor's. The account becomes platform staff:
    /// its token then says so, its connections see every tenant, and it may no longer sign in with
    /// a texted code alone.
    /// </summary>
    public async Task<PlatformChangeResult> GrantPlatformRoleAsync(Guid actor, Guid userId, string? roleCode, CancellationToken cancellationToken = default)
    {
        if (userId == actor)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.OwnAccount);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var role = await FindPlatformRoleAsync(roleCode, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.RoleNotFound);
        }

        if (!await OutranksAsync(actor, role.Rank, cancellationToken).ConfigureAwait(false))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.RankNotJunior);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.UserNotFound);
        }

        var held = await db.UserRoles
            .SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id && ur.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, InstitutionId = null, AssignedBy = actor, IsActive = true });
        }
        else
        {
            held.ValidFrom = null;
            held.ValidTo = null;
        }

        user.UserType = PlatformStaff;
        user.MfaRequired = true;
        // A token issued before says "not platform"; the next refresh must issue one that does.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PermissionsVersion++;
        Record(actor, "PlatformRoleGranted", userId, institutionId: null, role.Code);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return PlatformChangeResult.Success;
    }

    /// <summary>
    /// Takes a platform role away. With none left the account stops being platform staff, so its
    /// connections stop seeing every tenant; its stamp rotates so no token says otherwise.
    /// </summary>
    public async Task<PlatformChangeResult> RevokePlatformRoleAsync(Guid actor, Guid userId, string? roleCode, CancellationToken cancellationToken = default)
    {
        if (userId == actor)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.OwnAccount);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var role = await FindPlatformRoleAsync(roleCode, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.RoleNotFound);
        }

        if (!await OutranksAsync(actor, role.Rank, cancellationToken).ConfigureAwait(false))
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.RankNotJunior);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken).ConfigureAwait(false);
        var held = await db.UserRoles
            .SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id && ur.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || held is null)
        {
            return PlatformChangeResult.Failed(PlatformChangeFailure.UserNotFound);
        }

        held.IsActive = false;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (await PlatformRankAsync(userId, cancellationToken).ConfigureAwait(false) is null)
        {
            var staffSomewhere = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.IsActive && ur.InstitutionId != null, cancellationToken).ConfigureAwait(false);
            user.UserType = staffSomewhere ? "InstitutionStaff" : "Donor";
        }

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PermissionsVersion++;
        Record(actor, "PlatformRoleRevoked", userId, institutionId: null, role.Code);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return PlatformChangeResult.Success;
    }

    // ------------------------------------------------------------------ rank

    private sealed record PlatformRole(int Id, string Code, byte Rank);

    private Task<PlatformRole?> FindPlatformRoleAsync(string? code, CancellationToken cancellationToken)
    {
        var wanted = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return db.Roles
            .AsNoTracking()
            .Where(r => r.InstitutionId == null && r.Scope == PlatformScope && r.Code == wanted && r.IsActive)
            .Join(db.RoleLevels, r => r.RoleLevelId, l => l.Id, (r, l) => new PlatformRole(r.Id, r.Code, l.Rank))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>The user's most senior platform rank now, or null when they hold no platform role.</summary>
    private Task<byte?> PlatformRankAsync(Guid userId, CancellationToken cancellationToken)
    {
        var at = timeProvider.GetUtcNow().UtcDateTime;
        return db.UserRoles
            .Where(ur => ur.UserId == userId
                         && ur.InstitutionId == null
                         && ur.IsActive
                         && ur.Role.IsActive
                         && ur.Role.Scope == PlatformScope
                         && (ur.ValidFrom == null || ur.ValidFrom <= at)
                         && (ur.ValidTo == null || ur.ValidTo > at))
            .Join(db.RoleLevels, ur => ur.Role.RoleLevelId, l => l.Id, (ur, l) => (byte?)l.Rank)
            .MinAsync(cancellationToken);
    }

    /// <summary>The actor's rank is strictly more senior (a smaller number) than <paramref name="rank"/>.</summary>
    private async Task<bool> OutranksAsync(Guid actor, byte rank, CancellationToken cancellationToken) =>
        await PlatformRankAsync(actor, cancellationToken).ConfigureAwait(false) is { } own && own < rank;

    /// <summary>An outbox row in the same transaction: who did what to whom. Ids only.</summary>
    private void Record(Guid actor, string change, Guid? targetUserId, int? institutionId, string? roleCode = null) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = ChangedEvent,
            AggregateType = "Platform",
            AggregateId = (targetUserId?.ToString("D") ?? institutionId?.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? actor.ToString("D"),
            Payload = JsonSerializer.Serialize(new { change, userId = targetUserId, institutionId, roleCode, by = actor }),
            OccurredAt = timeProvider.GetUtcNow().UtcDateTime,
        });
}
