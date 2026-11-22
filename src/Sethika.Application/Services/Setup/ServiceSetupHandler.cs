using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Setup;

/// <summary>
/// What an institution sets up before it can take service requests (Phase E, 8.4): the services it
/// offers, its roster of religious leaders, their usual weekly hours and their leave.
/// </summary>
/// <remarks>
/// Every method acts for <c>institutionId</c>, the institution in the caller's token. Service types
/// are public to read under Row-Level Security, so a lookup also checks the row is that institution's;
/// another institution's reads as not found, as one that does not exist. Leaders and leave are tenant
/// rows (V9), and are named by institution all the same.
///
/// Changing a service type never changes an appointment already asked for: its end was fixed when it
/// was made. Taking a leader off the roster does not take them off the appointments they hold either;
/// the result names those, so the screen can warn and the institution reassigns.
///
/// The LEADER role follows the link (QA DEF-001). Its permissions, <c>SERVICES.MYSCHEDULE.*</c>,
/// are held by no other template role, so anti-escalation lets no owner or manager hand it out:
/// linking a leader to an account is the one way it is given, and unlinking - or linking the
/// record to someone else - the way it is taken back. It reaches only that person's own
/// assignments, whose details the leader's texts already carry. Taking a leader off the roster
/// keeps it: they may still hold appointments to answer for.
/// </remarks>
public sealed class ServiceSetupHandler(
    ISethikaDbContext db,
    ILeaderLocks locks,
    IPhoneNumberNormalizer phoneNumbers,
    TimeProvider timeProvider)
{
    private const string NameIndex = "UQ_ServiceTypes_Name";
    private const string AccountIndex = "UX_ReligiousLeaders_User";
    private const string BlocksKey = "PK_LeaderTimeBlocks";
    private const int MaxName = 100;
    private const int MaxTitle = 40;
    private const int MaxDescription = 300;
    private const int MaxReason = 200;
    private const int MaxPhone = 20;
    private const string LeaderRole = "LEADER";
    private const string RoleChangedEvent = "RolePermissionsChanged";

    /// <summary>The longest leave one request may add (2,976 blocks); longer is several, or the leader off the roster.</summary>
    public const int MaxTimeOffDays = 31;

    /// <summary>The most spans one leader's week may have: three a day.</summary>
    public const int MaxWeeklySpans = 21;

    private static readonly string[] Languages = ["en", "si", "ta"];

    private static readonly string[] LocationModes = [ServiceLocation.AtInstitution, ServiceLocation.AtDonorPlace, ServiceLocation.Either];

    // ------------------------------------------------------------------ service types

    /// <summary>A new, active service type.</summary>
    public async Task<SetupChangeResult> CreateTypeAsync(int institutionId, Guid userId, ServiceTypeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var invalid = new List<string>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var type = new ServiceType
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            CreatedAt = now,
            CreatedBy = userId,
        };

        Apply(input, type, invalid);
        if (invalid.Count > 0)
        {
            return SetupChangeResult.Invalid(invalid);
        }

        db.ServiceTypes.Add(type);
        return await SaveTypeAsync(type, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the type's details, if it is still the <see cref="ServiceTypeInput.Version"/> the editor read.</summary>
    public async Task<SetupChangeResult> UpdateTypeAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        ServiceTypeInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var type = await FindTypeAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (type is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.ServiceTypeNotFound);
        }

        var invalid = new List<string>();
        if (input.Version is not { Length: 8 })
        {
            invalid.Add("version");
        }

        Apply(input, type, invalid);
        if (invalid.Count > 0)
        {
            return SetupChangeResult.Invalid(invalid);
        }

        // The version the editor read, not the one loaded just now: an edit made in between makes
        // this one stale instead of silently overwriting it.
        db.Entry(type).Property(t => t.RowVersion).OriginalValue = input.Version!;
        type.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        type.UpdatedBy = userId;

        return await SaveTypeAsync(type, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops donors asking for a service, or lets them again. Appointments already asked for stay.</summary>
    public async Task<SetupChangeResult> SetTypeActiveAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var type = await FindTypeAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (type is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.ServiceTypeNotFound);
        }

        if (type.IsActive == active)
        {
            return SetupChangeResult.For(type.PublicId, type.RowVersion);
        }

        type.IsActive = active;
        type.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        type.UpdatedBy = userId;
        return await SaveTypeAsync(type, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ leaders

    /// <summary>A new, active leader on the roster.</summary>
    public async Task<SetupChangeResult> CreateLeaderAsync(int institutionId, Guid userId, LeaderInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leader = new ReligiousLeader
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            CreatedAt = now,
            CreatedBy = userId,
        };

        var refused = await ApplyAsync(input, leader, userId, [], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return refused;
        }

        db.ReligiousLeaders.Add(leader);
        return await SaveLeaderAsync(leader, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the leader's details, if it is still the <see cref="LeaderInput.Version"/> the editor read.</summary>
    public async Task<SetupChangeResult> UpdateLeaderAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        LeaderInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var leader = await FindLeaderAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (leader is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.LeaderNotFound);
        }

        var invalid = new List<string>();
        if (input.Version is not { Length: 8 })
        {
            invalid.Add("version");
        }

        var refused = await ApplyAsync(input, leader, userId, invalid, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return refused;
        }

        db.Entry(leader).Property(l => l.RowVersion).OriginalValue = input.Version!;
        leader.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        leader.UpdatedBy = userId;

        return await SaveLeaderAsync(leader, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes a leader off the roster, or puts them back. A leader off the roster cannot be assigned;
    /// the appointments they already hold are named, not changed.
    /// </summary>
    public async Task<SetupChangeResult> SetLeaderActiveAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var leader = await FindLeaderAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (leader is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.LeaderNotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var held = active ? [] : await HeldAppointmentsAsync(institutionId, leader.Id, now, cancellationToken).ConfigureAwait(false);

        if (leader.IsActive == active)
        {
            return SetupChangeResult.For(leader.PublicId, leader.RowVersion, held);
        }

        leader.IsActive = active;
        leader.UpdatedAt = now;
        leader.UpdatedBy = userId;
        return await SaveLeaderAsync(leader, held, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the leader's usual week. Only a filter for the picker (V2): it never refuses an
    /// assignment, so it needs no lock and no version.
    /// </summary>
    public async Task<SetupChangeResult> SetWeeklyHoursAsync(
        int institutionId,
        Guid publicId,
        IReadOnlyList<WeeklyHoursInput>? spans,
        CancellationToken cancellationToken = default)
    {
        var leaderId = await LeaderIdAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (leaderId is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.LeaderNotFound);
        }

        var rows = new List<LeaderWeeklyHours>();
        var invalid = new List<string>();

        if (spans is null || spans.Count > MaxWeeklySpans)
        {
            return SetupChangeResult.Invalid(["weeklyHours"]);
        }

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            // Two spans of one day that overlap say nothing two separate ones would not, and mean a
            // typing mistake more often than a plan.
            if (span?.DayOfWeek is not (<= 6 and var day)
                || Time(span.StartTime) is not { } start
                || Time(span.EndTime) is not { } end
                || end <= start
                || rows.Exists(r => r.DayOfWeek == day && r.StartTime < end && start < r.EndTime))
            {
                invalid.Add(string.Create(CultureInfo.InvariantCulture, $"weeklyHours[{i}]"));
                continue;
            }

            rows.Add(new LeaderWeeklyHours
            {
                LeaderId = leaderId.Value,
                InstitutionId = institutionId,
                DayOfWeek = day,
                StartTime = start,
                EndTime = end,
            });
        }

        if (invalid.Count > 0)
        {
            return SetupChangeResult.Invalid(invalid);
        }

        var current = await db.LeaderWeeklyHours
            .Where(h => h.LeaderId == leaderId && h.InstitutionId == institutionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // One SaveChanges, so one transaction: the week is replaced whole or not at all.
        db.LeaderWeeklyHours.RemoveRange(current);
        db.LeaderWeeklyHours.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SetupChangeResult.Ok();
    }

    // ------------------------------------------------------------------ leave

    /// <summary>
    /// Records leave and takes the leader's time for it (V2, flow 6). Leave over an appointment the
    /// leader holds is refused with those appointments named, so the institution reassigns first.
    /// </summary>
    /// <remarks>
    /// Blocks are written from the current block on, not from a start already past: the past holds
    /// nothing that can still be assigned. The leader's lock is taken as an assignment takes it, so a
    /// clash is named rather than found by the key; the key (<c>PK_LeaderTimeBlocks</c>) still refuses
    /// one if the lock is not held.
    /// </remarks>
    public async Task<SetupChangeResult> AddTimeOffAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        TimeOffInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var leaderId = await LeaderIdAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (leaderId is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.LeaderNotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invalid = new List<string>();
        var reason = Tidy(input.Reason);
        var startsAt = input.StartsAt?.UtcDateTime;
        var endsAt = input.EndsAt?.UtcDateTime;

        if (startsAt is not { } start || !TimeBlocks.IsAligned(start))
        {
            invalid.Add("startsAt");
        }

        if (endsAt is not { } end
            || !TimeBlocks.IsAligned(end)
            || end <= now
            || (startsAt is { } from && (end <= from || end - from > TimeSpan.FromDays(MaxTimeOffDays))))
        {
            invalid.Add("endsAt");
        }

        if (reason is { Length: > MaxReason })
        {
            invalid.Add("reason");
        }

        if (invalid.Count > 0)
        {
            return SetupChangeResult.Invalid(invalid);
        }

        var current = new DateTime(now.Ticks - (now.Ticks % TimeSpan.FromMinutes(TimeBlocks.Minutes).Ticks), DateTimeKind.Utc);
        var blocks = TimeBlocks.For(startsAt!.Value < current ? current : startsAt.Value, endsAt!.Value);
        var first = blocks[0];
        var last = blocks[^1];

        await using var lease = await locks.AcquireAsync([leaderId.Value], cancellationToken).ConfigureAwait(false);
        if (lease.Outcome == LockOutcome.Busy)
        {
            return SetupChangeResult.Failed(ServiceErrors.TryAgain);
        }

        var clash = await ClashAsync(leaderId.Value, first, last, cancellationToken).ConfigureAwait(false);
        if (clash is not null)
        {
            return clash;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var timeOff = new LeaderTimeOff
        {
            LeaderId = leaderId.Value,
            InstitutionId = institutionId,
            StartsAt = startsAt.Value,
            EndsAt = endsAt.Value,
            Reason = reason,
            CreatedAt = now,
            CreatedBy = userId,
        };

        try
        {
            // The leave first: every block names it (FK_LeaderTimeBlocks_TimeOff).
            db.LeaderTimeOff.Add(timeOff);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            db.LeaderTimeBlocks.AddRange(blocks.Select(block => new LeaderTimeBlock
            {
                LeaderId = leaderId.Value,
                BlockStart = block,
                InstitutionId = institutionId,
                Kind = LeaderTimeBlockKind.TimeOff,
                TimeOffId = timeOff.Id,
            }));

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == BlocksKey)
        {
            // Taken after the read above: only possible without the lock. Who took it is not known here.
            return SetupChangeResult.Failed(ServiceErrors.TimeOffClashes);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return SetupChangeResult.ForTimeOff(timeOff.Id);
    }

    /// <summary>Removes leave, and gives the leader's time back.</summary>
    public async Task<SetupChangeResult> RemoveTimeOffAsync(int institutionId, int timeOffId, CancellationToken cancellationToken = default)
    {
        var timeOff = await db.LeaderTimeOff
            .SingleOrDefaultAsync(t => t.Id == timeOffId && t.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (timeOff is null)
        {
            return SetupChangeResult.Failed(SetupChangeResult.TimeOffNotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await db.LeaderTimeBlocks
            .Where(b => b.TimeOffId == timeOff.Id && b.LeaderId == timeOff.LeaderId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        db.LeaderTimeOff.Remove(timeOff);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return SetupChangeResult.Ok();
    }

    // ------------------------------------------------------------------ plumbing

    private Task<ServiceType?> FindTypeAsync(int institutionId, Guid publicId, CancellationToken cancellationToken) =>
        db.ServiceTypes.SingleOrDefaultAsync(t => t.PublicId == publicId && t.InstitutionId == institutionId, cancellationToken);

    private Task<ReligiousLeader?> FindLeaderAsync(int institutionId, Guid publicId, CancellationToken cancellationToken) =>
        db.ReligiousLeaders.SingleOrDefaultAsync(l => l.PublicId == publicId && l.InstitutionId == institutionId, cancellationToken);

    private Task<int?> LeaderIdAsync(int institutionId, Guid publicId, CancellationToken cancellationToken) =>
        db.ReligiousLeaders
            .Where(l => l.PublicId == publicId && l.InstitutionId == institutionId)
            .Select(l => (int?)l.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<SetupChangeResult> SaveTypeAsync(ServiceType type, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == NameIndex)
        {
            return SetupChangeResult.Failed(SetupChangeResult.NameTaken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SetupChangeResult.Failed(SetupChangeResult.Stale);
        }

        return SetupChangeResult.For(type.PublicId, type.RowVersion);
    }

    private async Task<SetupChangeResult> SaveLeaderAsync(ReligiousLeader leader, IReadOnlyList<string> held, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == AccountIndex)
        {
            return SetupChangeResult.Failed(SetupChangeResult.AccountAlreadyLinked);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SetupChangeResult.Failed(SetupChangeResult.Stale);
        }

        return SetupChangeResult.For(leader.PublicId, leader.RowVersion, held);
    }

    /// <summary>The appointments, not yet over, that <paramref name="leaderId"/> is a live leader of.</summary>
    private async Task<IReadOnlyList<string>> HeldAppointmentsAsync(int institutionId, int leaderId, DateTime now, CancellationToken cancellationToken) =>
        await db.ServiceAppointments
            .Where(a => a.InstitutionId == institutionId
                        && (a.Status == AppointmentStatus.Requested || a.Status == AppointmentStatus.Approved)
                        && a.EndsAt > now
                        && a.Leaders.Any(l => l.LeaderId == leaderId
                                              && (l.Response == AssignmentResponse.Pending || l.Response == AssignmentResponse.Accepted)))
            .OrderBy(a => a.StartsAt)
            .Select(a => a.AppointmentNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Why the leader's blocks from <paramref name="first"/> to <paramref name="last"/> are not free:
    /// the appointments in the way, or leave already there. <see langword="null"/> when they are free.
    /// </summary>
    private async Task<SetupChangeResult?> ClashAsync(int leaderId, DateTime first, DateTime last, CancellationToken cancellationToken)
    {
        var taken = await db.LeaderTimeBlocks
            .AsNoTracking()
            .Where(b => b.LeaderId == leaderId && b.BlockStart >= first && b.BlockStart <= last)
            .Select(b => new { b.Kind, b.AppointmentId })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (taken.Count == 0)
        {
            return null;
        }

        var appointmentIds = taken.Where(b => b.AppointmentId is not null).Select(b => b.AppointmentId!.Value).ToList();
        if (appointmentIds.Count == 0)
        {
            return SetupChangeResult.Failed(SetupChangeResult.TimeOffOverlaps);
        }

        var numbers = await db.ServiceAppointments
            .AsNoTracking()
            .Where(a => appointmentIds.Contains(a.Id))
            .OrderBy(a => a.StartsAt)
            .Select(a => a.AppointmentNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return SetupChangeResult.Failed(ServiceErrors.TimeOffClashes, numbers);
    }

    /// <summary>
    /// Checks the form and copies it onto <paramref name="type"/>, naming every field that is wrong.
    /// The table's CHECKs say the same, but a 547 cannot tell the screen which field to mark.
    /// </summary>
    private static void Apply(ServiceTypeInput input, ServiceType type, List<string> invalid)
    {
        var name = Tidy(input.Name);
        if (name is null || name.Length > MaxName)
        {
            invalid.Add("name");
        }

        var nameSi = Tidy(input.NameSi);
        if (nameSi is { Length: > MaxName })
        {
            invalid.Add("nameSi");
        }

        var nameTa = Tidy(input.NameTa);
        if (nameTa is { Length: > MaxName })
        {
            invalid.Add("nameTa");
        }

        var description = Tidy(input.Description);
        if (description is { Length: > MaxDescription })
        {
            invalid.Add("description");
        }

        if (input.DurationMinutes is not (>= 15 and <= 720 and var duration) || !TimeBlocks.IsWhole(duration))
        {
            invalid.Add("durationMinutes");
        }

        var buffer = input.BufferMinutes ?? 0;
        if (buffer is < 0 or > 180 || !TimeBlocks.IsWhole(buffer))
        {
            invalid.Add("bufferMinutes");
        }

        if (input.LeadersRequired is not (>= 1 and <= 20))
        {
            invalid.Add("leadersRequired");
        }

        var location = input.LocationMode?.Trim();
        if (location is null || !LocationModes.Contains(location, StringComparer.Ordinal))
        {
            invalid.Add("locationMode");
        }

        var minAdvance = input.MinAdvanceDays ?? 1;
        if (minAdvance < 0)
        {
            invalid.Add("minAdvanceDays");
        }

        var maxAdvance = input.MaxAdvanceDays ?? 180;
        if (maxAdvance < Math.Max(minAdvance, (short)0))
        {
            invalid.Add("maxAdvanceDays");
        }

        if (invalid.Count > 0)
        {
            return;
        }

        type.Name = name!;
        type.NameSi = nameSi;
        type.NameTa = nameTa;
        type.Description = description;
        type.DurationMinutes = input.DurationMinutes!.Value;
        type.BufferMinutes = buffer;
        type.LeadersRequired = input.LeadersRequired!.Value;
        type.LocationMode = location!;
        type.MinAdvanceDays = minAdvance;
        type.MaxAdvanceDays = maxAdvance;
    }

    /// <summary>
    /// Checks the form and copies it onto <paramref name="leader"/>: the refusal, or
    /// <see langword="null"/> when it is copied. Linking looks the account up by its phone number.
    /// </summary>
    /// <param name="actor">Who is saving the form: named on a LEADER role the link gives.</param>
    /// <param name="invalid">Fields already found wrong by the caller; nothing is copied if any are.</param>
    private async Task<SetupChangeResult?> ApplyAsync(LeaderInput input, ReligiousLeader leader, Guid actor, List<string> invalid, CancellationToken cancellationToken)
    {
        var name = Tidy(input.DisplayName);
        if (name is null || name.Length > MaxName)
        {
            invalid.Add("displayName");
        }

        var nameSi = Tidy(input.NameSi);
        if (nameSi is { Length: > MaxName })
        {
            invalid.Add("nameSi");
        }

        var nameTa = Tidy(input.NameTa);
        if (nameTa is { Length: > MaxName })
        {
            invalid.Add("nameTa");
        }

        var title = Tidy(input.Title);
        if (title is { Length: > MaxTitle })
        {
            invalid.Add("title");
        }

        // A phone that cannot be texted is refused here rather than after paying to send to it.
        string? phone = null;
        if (Tidy(input.Phone) is { } typed)
        {
            if (phoneNumbers.TryNormalizeMobile(typed, out var normalized) && normalized.Length <= MaxPhone)
            {
                phone = normalized;
            }
            else
            {
                invalid.Add("phone");
            }
        }
        else if (input.LinkAccount == true)
        {
            invalid.Add("phone");
        }

        var language = Tidy(input.Language) ?? "si";
        if (!Languages.Contains(language, StringComparer.Ordinal))
        {
            invalid.Add("language");
        }

        if (invalid.Count > 0)
        {
            return SetupChangeResult.Invalid(invalid);
        }

        Guid? account = null;
        if (input.LinkAccount == true)
        {
            account = await db.Users
                .Where(u => u.PhoneNumber == phone)
                .Select(u => (Guid?)u.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (account is null)
            {
                return SetupChangeResult.Failed(SetupChangeResult.AccountNotFound);
            }
        }

        leader.DisplayName = name!;
        leader.NameSi = nameSi;
        leader.NameTa = nameTa;
        leader.Title = title;
        leader.Phone = phone;
        leader.Language = language;

        if (leader.UserId != account)
        {
            await MoveLeaderRoleAsync(leader.InstitutionId, actor, leader.UserId, account, cancellationToken).ConfigureAwait(false);
            leader.UserId = account;
        }

        return null;
    }

    /// <summary>
    /// Takes the institution's LEADER role from the account the record was linked to and gives it
    /// to the one it is linked to now. Tracked only, so it is saved - or refused, as stale or as an
    /// account already linked - together with the leader.
    /// </summary>
    private async Task MoveLeaderRoleAsync(int institutionId, Guid actor, Guid? from, Guid? to, CancellationToken cancellationToken)
    {
        // Every institution gets a LEADER copy at approval; one without (switched off) gives nothing.
        var roleId = await db.Roles
            .Where(r => r.InstitutionId == institutionId && r.Code == LeaderRole && r.IsActive)
            .Select(r => (int?)r.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (roleId is not { } role)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (from is { } previous)
        {
            // Deactivated rather than deleted, as RevokeAsync does: the temporal history keeps who held it.
            var held = await HeldLeaderRoleAsync(previous, role, cancellationToken).ConfigureAwait(false);
            if (held is not null)
            {
                held.IsActive = false;
                await ChangedAsync(institutionId, actor, previous, role, "LeaderUnlinked", now, cancellationToken).ConfigureAwait(false);
            }
        }

        if (to is { } account)
        {
            // An active row whose validity window lapsed is reopened: UX_UserRoles allows one per user and role.
            var held = await HeldLeaderRoleAsync(account, role, cancellationToken).ConfigureAwait(false);
            if (held is null)
            {
                db.UserRoles.Add(new UserRole { UserId = account, RoleId = role, InstitutionId = institutionId, AssignedBy = actor, IsActive = true });
            }
            else
            {
                held.ValidFrom = null;
                held.ValidTo = null;
            }

            await ChangedAsync(institutionId, actor, account, role, "LeaderLinked", now, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<UserRole?> HeldLeaderRoleAsync(Guid userId, int roleId, CancellationToken cancellationToken) =>
        db.UserRoles.SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId && ur.IsActive, cancellationToken);

    /// <summary>
    /// The user's version is in their permission cache key, so their next request reloads; and the
    /// outbox row every role change writes, ids only.
    /// </summary>
    private async Task ChangedAsync(int institutionId, Guid actor, Guid userId, int roleId, string change, DateTime at, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        user.PermissionsVersion++;

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = RoleChangedEvent,
            AggregateType = "Role",
            AggregateId = roleId.ToString(CultureInfo.InvariantCulture),
            Payload = JsonSerializer.Serialize(new { institutionId, roleCode = LeaderRole, change, userId, by = actor }),
            OccurredAt = at,
        });
    }

    /// <summary><c>HH:mm</c>, as an HTML time input sends it; the column keeps no seconds.</summary>
    private static TimeOnly? Time(string? value) =>
        TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
