using Sethika.Domain.Bookings;

namespace Sethika.Domain.Services;

/// <summary>
/// A donor's request for a religious service, and the leaders assigned to it (Phase E, promises 2
/// and 4). Maps <c>booking.ServiceAppointments</c>.
/// </summary>
/// <remarks>
/// The aggregate owns its status and its assignments, like <see cref="Booking"/>: every change is a
/// method that refuses with a <see cref="ServiceErrors"/> code when it is not allowed, and every
/// status change writes a <see cref="StatusHistory"/> row.
///
/// What it does not own is the leaders' time. The blocks in <c>booking.LeaderTimeBlocks</c> are
/// written and deleted by the handler in the same transaction, and their primary key is what keeps a
/// leader in one place at a time (V2). A method says which leaders gained or lost time through
/// <see cref="AppointmentLeader.IsLive"/> before and after.
/// </remarks>
public class ServiceAppointment
{
    public long Id { get; set; }
    public string AppointmentNo { get; set; } = null!;
    public int InstitutionId { get; set; }
    public int ServiceTypeId { get; set; }
    public Guid DonorUserId { get; set; }

    /// <summary>Snapshot of the donor's name when they asked.</summary>
    public string DonorName { get; set; } = null!;

    /// <summary>Snapshot of the donor's phone. Masked in the database.</summary>
    public string DonorPhone { get; set; } = null!;

    /// <summary>UTC, on the grid.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>UTC: <see cref="StartsAt"/> plus the service's duration.</summary>
    public DateTime EndsAt { get; set; }

    /// <summary><see cref="ServiceLocation.AtInstitution"/> or <see cref="ServiceLocation.AtDonorPlace"/>.</summary>
    public string LocationMode { get; set; } = ServiceLocation.AtInstitution;

    public string? Address { get; set; }
    public string? Occasion { get; set; }
    public string? DonorNote { get; set; }

    public string Status { get; private set; } = AppointmentStatus.Requested;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? RejectReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<AppointmentLeader> Leaders { get; } = new List<AppointmentLeader>();
    public ICollection<AppointmentStatusChange> StatusHistory { get; } = new List<AppointmentStatusChange>();

    /// <summary>The longest reason: <c>RejectReason</c>, <c>CancelReason</c> and <c>DeclineReason</c> are <c>NVARCHAR(300)</c>.</summary>
    public const int MaxReason = 300;

    /// <summary>The leaders who are still going: assigned and not declined or removed.</summary>
    public IEnumerable<AppointmentLeader> LiveLeaders => Leaders.Where(l => l.IsLive);

    /// <summary>
    /// Whether an approved appointment has fewer live leaders than <paramref name="leadersRequired"/>:
    /// computed, never stored (V6). A decline makes it true again.
    /// </summary>
    public bool NeedsLeaders(int leadersRequired) => NeedsLeaders(Status, LiveLeaders.Count(), leadersRequired);

    /// <summary>The same rule from a list's columns: <see cref="NeedsLeaders(int)"/> for a row read with Dapper.</summary>
    public static bool NeedsLeaders(string status, int liveLeaders, int leadersRequired) =>
        status is AppointmentStatus.Requested or AppointmentStatus.Approved && liveLeaders < leadersRequired;

    /// <summary>
    /// A new request by the signed-in donor, <see cref="AppointmentStatus.Requested"/>. It holds no
    /// leader yet.
    /// </summary>
    /// <remarks><see cref="ServiceRules.CanRequest"/> is the caller's to have checked.</remarks>
    public static ServiceAppointment Request(
        string appointmentNo,
        ServiceType type,
        DateTime startsAt,
        string location,
        string? address,
        string? occasion,
        string? donorNote,
        Guid donorUserId,
        string donorName,
        string donorPhone,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appointmentNo);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(donorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(donorPhone);

        var appointment = new ServiceAppointment
        {
            AppointmentNo = appointmentNo,
            InstitutionId = type.InstitutionId,
            ServiceTypeId = type.Id,
            DonorUserId = donorUserId,
            DonorName = donorName,
            DonorPhone = donorPhone,
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(type.DurationMinutes),
            LocationMode = location,
            Address = location == ServiceLocation.AtDonorPlace ? address?.Trim() : null,
            Occasion = string.IsNullOrWhiteSpace(occasion) ? null : occasion.Trim(),
            DonorNote = string.IsNullOrWhiteSpace(donorNote) ? null : donorNote.Trim(),
            CreatedAt = now,
            CreatedBy = donorUserId,
        };

        appointment.StatusHistory.Add(new AppointmentStatusChange
        {
            FromStatus = null,
            ToStatus = AppointmentStatus.Requested,
            ChangedBy = donorUserId,
            ChangedAt = now,
        });

        return appointment;
    }

    // ------------------------------------------------------------------ leaders

    /// <summary>
    /// Assigns <paramref name="leaderIds"/>, each <see cref="AssignmentResponse.Pending"/>, and approves
    /// a request (Requested → Approved). The handler writes each leader's blocks in the same
    /// transaction; a clash there refuses the whole assignment.
    /// </summary>
    /// <param name="leadersRequired">The service type's <c>LeadersRequired</c>: the most live leaders allowed.</param>
    public ServiceResult Assign(IReadOnlyCollection<int> leaderIds, int leadersRequired, Guid userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(leaderIds);

        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Approved) || leaderIds.Count == 0)
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        if (now >= EndsAt)
        {
            return ServiceResult.Fail(ServiceErrors.AlreadyOver);
        }

        if (leaderIds.Distinct().Count() != leaderIds.Count || leaderIds.Any(id => LiveLeaders.Any(l => l.LeaderId == id)))
        {
            return ServiceResult.Fail(ServiceErrors.AlreadyAssigned);
        }

        if (LiveLeaders.Count() + leaderIds.Count > leadersRequired)
        {
            return ServiceResult.Fail(ServiceErrors.TooManyLeaders);
        }

        foreach (var leaderId in leaderIds)
        {
            // A leader who declined or was removed keeps their row (the primary key is the pair), so
            // assigning them again reopens it.
            var row = Leaders.SingleOrDefault(l => l.LeaderId == leaderId);
            if (row is null)
            {
                row = new AppointmentLeader { AppointmentId = Id, LeaderId = leaderId, InstitutionId = InstitutionId };
                Leaders.Add(row);
            }

            row.Response = AssignmentResponse.Pending;
            row.AssignedBy = userId;
            row.AssignedAt = now;
            row.RespondedBy = null;
            row.RespondedAt = null;
            row.RespondedOnBehalf = false;
            row.DeclineReason = null;
        }

        if (Status == AppointmentStatus.Requested)
        {
            DecidedBy = userId;
            DecidedAt = now;
            MoveTo(AppointmentStatus.Approved, userId, now, comment: null);
        }

        return ServiceResult.Ok;
    }

    /// <summary>Takes a live leader off the appointment (<see cref="AssignmentResponse.Removed"/>). The handler deletes their blocks.</summary>
    public ServiceResult Remove(int leaderId, Guid userId, DateTime now)
    {
        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Approved))
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        var row = LiveLeaders.SingleOrDefault(l => l.LeaderId == leaderId);
        if (row is null)
        {
            return ServiceResult.Fail(ServiceErrors.NotAssigned);
        }

        row.Response = AssignmentResponse.Removed;
        row.RespondedBy = userId;
        row.RespondedAt = now;
        return ServiceResult.Ok;
    }

    /// <summary>
    /// A leader's answer, given by the leader or by an administrator for them. Accepting is from
    /// <see cref="AssignmentResponse.Pending"/>; declining is from Pending or Accepted, and the handler
    /// deletes that leader's blocks, so the appointment needs a leader again.
    /// </summary>
    public ServiceResult Respond(int leaderId, bool accept, string? reason, Guid userId, bool onBehalf, DateTime now)
    {
        if (Status != AppointmentStatus.Approved)
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        if (now >= EndsAt)
        {
            return ServiceResult.Fail(ServiceErrors.AlreadyOver);
        }

        var row = LiveLeaders.SingleOrDefault(l => l.LeaderId == leaderId);
        if (row is null)
        {
            return ServiceResult.Fail(ServiceErrors.NotAssigned);
        }

        if (accept && row.Response != AssignmentResponse.Pending)
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        if (reason?.Trim().Length > MaxReason)
        {
            return ServiceResult.Fail(ServiceErrors.ReasonTooLong);
        }

        row.Response = accept ? AssignmentResponse.Accepted : AssignmentResponse.Declined;
        row.RespondedBy = userId;
        row.RespondedAt = now;
        row.RespondedOnBehalf = onBehalf;
        row.DeclineReason = accept || string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return ServiceResult.Ok;
    }

    // ------------------------------------------------------------------ the institution's decision

    /// <summary>Requested → Rejected, with the reason the donor will read.</summary>
    public ServiceResult Reject(string reason, Guid userId, DateTime now)
    {
        if (Status != AppointmentStatus.Requested)
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        var refused = RefuseReason(reason, required: true);
        if (refused is not null)
        {
            return ServiceResult.Fail(refused);
        }

        DecidedBy = userId;
        DecidedAt = now;
        RejectReason = reason.Trim();
        MoveTo(AppointmentStatus.Rejected, userId, now, RejectReason);
        return ServiceResult.Ok;
    }

    // ------------------------------------------------------------------ cancelling

    /// <summary>
    /// Requested or Approved → Cancelled, by the donor who asked, until the end of the day before
    /// (Colombo, V13). The leaders' blocks are released by the outbox (V16).
    /// </summary>
    public ServiceResult CancelAsDonor(string? reason, Guid userId, DateTime now)
    {
        if (DonorUserId != userId)
        {
            return ServiceResult.Fail(ServiceErrors.NotDonor);
        }

        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Approved))
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        if (!DonorMayCancel(Status, StartsAt, now))
        {
            return ServiceResult.Fail(ServiceErrors.CancelWindowClosed);
        }

        var refused = RefuseReason(reason, required: false);
        if (refused is not null)
        {
            return ServiceResult.Fail(refused);
        }

        Cancel(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), userId, now);
        return ServiceResult.Ok;
    }

    /// <summary>
    /// Whether the donor may still cancel an appointment in <paramref name="status"/> starting at
    /// <paramref name="startsAt"/>: what <see cref="CancelAsDonor"/> allows, for the screen's button.
    /// </summary>
    public static bool DonorMayCancel(string status, DateTime startsAt, DateTime utcNow) =>
        status is AppointmentStatus.Requested or AppointmentStatus.Approved
        && SriLankaTime.Today(utcNow) < SriLankaTime.Today(startsAt);

    /// <summary>Requested or Approved → Cancelled, by the institution, with a reason, until the service ends.</summary>
    public ServiceResult CancelAsInstitution(string reason, Guid userId, DateTime now)
    {
        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Approved))
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        if (now >= EndsAt)
        {
            return ServiceResult.Fail(ServiceErrors.AlreadyOver);
        }

        var refused = RefuseReason(reason, required: true);
        if (refused is not null)
        {
            return ServiceResult.Fail(refused);
        }

        Cancel(reason.Trim(), userId, now);
        return ServiceResult.Ok;
    }

    // ------------------------------------------------------------------ after the service

    /// <summary>Approved → Completed, once the service has ended (a CHECK cannot read the clock).</summary>
    public ServiceResult Complete(Guid userId, DateTime now)
    {
        if (Status != AppointmentStatus.Approved)
        {
            return ServiceResult.Fail(ServiceErrors.InvalidTransition);
        }

        if (now < EndsAt)
        {
            return ServiceResult.Fail(ServiceErrors.TooEarly);
        }

        CompletedAt = now;
        MoveTo(AppointmentStatus.Completed, userId, now, comment: null);
        return ServiceResult.Ok;
    }

    // ------------------------------------------------------------------ plumbing

    private static string? RefuseReason(string? reason, bool required)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return required ? ServiceErrors.ReasonRequired : null;
        }

        return reason.Trim().Length > MaxReason ? ServiceErrors.ReasonTooLong : null;
    }

    private void Cancel(string? reason, Guid userId, DateTime now)
    {
        CancelledBy = userId;
        CancelledAt = now;
        CancelReason = reason;
        MoveTo(AppointmentStatus.Cancelled, userId, now, reason);
    }

    private void MoveTo(string status, Guid userId, DateTime now, string? comment)
    {
        StatusHistory.Add(new AppointmentStatusChange
        {
            AppointmentId = Id,
            FromStatus = Status,
            ToStatus = status,
            ChangedBy = userId,
            ChangedAt = now,
            Comment = comment,
        });

        Status = status;
    }
}

/// <summary>
/// <c>booking.ServiceAppointments.Status</c> (V6).
/// </summary>
/// <remarks>
/// <code>
///            ┌─assign leaders─▶ Approved ─complete─▶ Completed
/// Requested ─┤
///            └─reject─────────▶ Rejected
/// Requested / Approved ─cancel─▶ Cancelled
/// </code>
/// "Needs leaders" is not a status: it is computed from the assignments (V6).
/// </remarks>
public static class AppointmentStatus
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
}

/// <summary>One leader on one appointment, and their answer. Maps <c>booking.AppointmentLeaders</c>.</summary>
public class AppointmentLeader
{
    public long AppointmentId { get; set; }
    public int LeaderId { get; set; }
    public int InstitutionId { get; set; }

    /// <summary>An <see cref="AssignmentResponse"/>.</summary>
    public string Response { get; set; } = AssignmentResponse.Pending;

    public Guid AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? RespondedBy { get; set; }
    public DateTime? RespondedAt { get; set; }

    /// <summary>An administrator answered for the leader (V1).</summary>
    public bool RespondedOnBehalf { get; set; }

    public string? DeclineReason { get; set; }

    /// <summary>Still going: the leader's time is blocked for it.</summary>
    public bool IsLive => AssignmentResponse.IsLive(Response);
}

/// <summary><c>booking.AppointmentLeaders.Response</c>.</summary>
public static class AssignmentResponse
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Removed = "Removed";

    /// <summary>Whether a leader with this answer still holds the time (their blocks exist).</summary>
    public static bool IsLive(string response) => response is Pending or Accepted;
}

/// <summary>One status change of an appointment. Maps <c>booking.AppointmentStatusHistory</c>.</summary>
public class AppointmentStatusChange
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }

    /// <summary><see langword="null"/> for the request being made.</summary>
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Comment { get; set; }
}
