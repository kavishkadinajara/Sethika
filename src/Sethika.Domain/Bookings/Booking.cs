namespace Sethika.Domain.Bookings;

/// <summary>
/// A donor's booking of one place on a slot-day. Maps <c>booking.Bookings</c>.
/// </summary>
/// <remarks>
/// The aggregate owns its status, like <c>RegistrationRequest</c>: every transition (C5) is a method
/// that checks the move is allowed from the current status, refuses with a
/// <see cref="BookingErrors"/> code otherwise, and writes a <see cref="StatusHistory"/> row. Status
/// and decision fields have private setters, so a handler cannot move a booking by assignment.
///
/// What it does not do is the place it holds on the slot-day. That counter lives on
/// <see cref="SlotInstance"/> and is changed by the engine's conditional UPDATE, in the same
/// transaction. A transition says whether it gave the place back through
/// <see cref="BookingStatus.HoldsPlace"/> before and after; the handler releases when that changes.
///
/// Who may ask is decided before the aggregate is reached: the endpoint's permission, and Row-Level
/// Security, which shows an institution only its own bookings. The one rule left here is the
/// donor's: only the donor who made a booking may cancel it as a donor.
/// </remarks>
public class Booking
{
    public long Id { get; set; }
    public string BookingNo { get; set; } = null!;
    public int InstitutionId { get; set; }
    public long SlotInstanceId { get; set; }

    /// <summary>The signed-in donor who booked (C6).</summary>
    public Guid? DonorUserId { get; set; }

    /// <summary>Snapshot of the donor's name when they booked, for receipts.</summary>
    public string DonorName { get; set; } = null!;

    /// <summary>Snapshot of the donor's phone. Masked in the database; <c>UX_Bookings_DonorPerSlot</c> keys on it.</summary>
    public string DonorPhone { get; set; } = null!;

    public short Headcount { get; set; }

    /// <summary><see cref="Bookings.FulfilmentMode"/>.</summary>
    public string FulfilmentMode { get; set; } = Bookings.FulfilmentMode.DonorBrings;

    public int? OccasionLookupId { get; set; }
    public string? InMemoryOf { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// <c>Online</c> when the donor booked it, <c>Recurring</c> when the yearly job did (plan 07, H2),
    /// <c>Memorial</c> when the donor booked it for an observance in a memorial plan (H6).
    /// </summary>
    public string Source { get; set; } = OnlineSource;

    /// <summary>The memorial plan it was booked for: by the donor from an observance, or by the yearly job for one.</summary>
    public int? MemorialPlanId { get; set; }

    /// <summary>The observance it was booked for; <see langword="null"/> on a year the yearly job made.</summary>
    public short? MemorialRuleId { get; set; }

    /// <summary>The yearly booking this one belongs to: the one it started, or the one that made it.</summary>
    public int? RecurringBookingId { get; set; }

    /// <summary>Set to add a new yearly booking with this one, in the same save.</summary>
    public RecurringBooking? RecurringBooking { get; set; }

    /// <summary>The institution's donor it is for: a standing day's sponsor (plan 08, K3).</summary>
    public int? InstitutionDonorId { get; set; }

    /// <summary>The standing sponsorship whose day it is (K3).</summary>
    public int? StandingSponsorshipId { get; set; }

    public string Status { get; private set; } = BookingStatus.Requested;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? RejectReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<BookingStatusChange> StatusHistory { get; } = new List<BookingStatusChange>();

    /// <summary><c>booking.Bookings.Source</c> for a donor booking on the site.</summary>
    public const string OnlineSource = "Online";

    /// <summary><c>booking.Bookings.Source</c> for a year's booking made by the yearly job.</summary>
    public const string RecurringSource = "Recurring";

    /// <summary><c>booking.Bookings.Source</c> for a donor booking made for a memorial plan's observance.</summary>
    public const string MemorialSource = "Memorial";

    /// <summary><c>booking.Bookings.Source</c> for a day of an institution's standing sponsorship (plan 08, K3).</summary>
    public const string StandingSource = "Standing";

    /// <summary>The longest reject or cancel reason: <c>RejectReason</c> and <c>CancelReason</c> are <c>NVARCHAR(300)</c>.</summary>
    public const int MaxReason = 300;

    /// <summary>
    /// A new booking by the signed-in donor: <see cref="BookingStatus.Requested"/>, or straight to
    /// <see cref="BookingStatus.Approved"/> when the slot needs no approval (C1).
    /// </summary>
    /// <remarks>Bookability (<see cref="MealSlotRules"/>) and the place on the slot-day are the caller's to have checked.</remarks>
    public static Booking Request(
        string bookingNo,
        MealSlot slot,
        long slotInstanceId,
        Guid donorUserId,
        string donorName,
        string donorPhone,
        short headcount,
        string fulfilment,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookingNo);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(donorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(donorPhone);

        var status = slot.RequiresApproval ? BookingStatus.Requested : BookingStatus.Approved;

        var booking = new Booking
        {
            BookingNo = bookingNo,
            InstitutionId = slot.InstitutionId,
            SlotInstanceId = slotInstanceId,
            DonorUserId = donorUserId,
            DonorName = donorName,
            DonorPhone = donorPhone,
            Headcount = headcount,
            FulfilmentMode = fulfilment,
            Status = status,
            CreatedBy = donorUserId,
            CreatedAt = now,
            DecidedAt = slot.RequiresApproval ? null : now,
        };

        booking.StatusHistory.Add(new BookingStatusChange
        {
            FromStatus = null,
            ToStatus = status,
            ChangedBy = donorUserId,
            ChangedAt = now,
            Comment = slot.RequiresApproval ? null : "The slot needs no approval.",
        });

        return booking;
    }

    /// <summary>
    /// A day of <paramref name="sponsorship"/>, booked by the institution for its own donor (plan 08,
    /// K3): <see cref="BookingStatus.Approved"/> at once, whatever the slot's approval setting, because
    /// the institution made the arrangement itself.
    /// </summary>
    /// <param name="donorUserId">The donor's account when the donor book links one (K14), so the day is in their My bookings.</param>
    /// <param name="madeBy">Who it is recorded as made by: the staff member who set the sponsorship up.</param>
    /// <remarks>Bookability (<see cref="MealSlotRules"/>) and the place on the slot-day are the caller's to have checked.</remarks>
    public static Booking Standing(
        string bookingNo,
        StandingSponsorship sponsorship,
        long slotInstanceId,
        Guid? donorUserId,
        string donorName,
        string donorPhone,
        Guid madeBy,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookingNo);
        ArgumentNullException.ThrowIfNull(sponsorship);
        ArgumentException.ThrowIfNullOrWhiteSpace(donorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(donorPhone);

        var booking = new Booking
        {
            BookingNo = bookingNo,
            InstitutionId = sponsorship.InstitutionId,
            SlotInstanceId = slotInstanceId,
            DonorUserId = donorUserId,
            InstitutionDonorId = sponsorship.InstitutionDonorId,
            StandingSponsorshipId = sponsorship.Id,
            Source = StandingSource,
            DonorName = donorName,
            DonorPhone = donorPhone,
            Headcount = sponsorship.Headcount,
            FulfilmentMode = sponsorship.FulfilmentMode,
            InMemoryOf = sponsorship.InMemoryOf,
            Status = BookingStatus.Approved,
            CreatedBy = madeBy,
            CreatedAt = now,
            DecidedBy = madeBy,
            DecidedAt = now,
        };

        booking.StatusHistory.Add(new BookingStatusChange
        {
            FromStatus = null,
            ToStatus = BookingStatus.Approved,
            ChangedBy = madeBy,
            ChangedAt = now,
            Comment = "A day of the institution's standing sponsorship.",
        });

        return booking;
    }

    // ------------------------------------------------------------------ the institution's decision

    /// <summary>Requested → Approved.</summary>
    public BookingResult Approve(Guid userId, DateTime now)
    {
        if (Status != BookingStatus.Requested)
        {
            return BookingResult.Fail(BookingErrors.InvalidTransition);
        }

        DecidedBy = userId;
        DecidedAt = now;
        MoveTo(BookingStatus.Approved, userId, now, comment: null);
        return BookingResult.Ok;
    }

    /// <summary>Requested → Rejected, with the reason the donor will read. Gives the place back.</summary>
    public BookingResult Reject(string reason, Guid userId, DateTime now)
    {
        if (Status != BookingStatus.Requested)
        {
            return BookingResult.Fail(BookingErrors.InvalidTransition);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return BookingResult.Fail(BookingErrors.ReasonRequired);
        }

        if (reason.Trim().Length > MaxReason)
        {
            return BookingResult.Fail(BookingErrors.ReasonTooLong);
        }

        DecidedBy = userId;
        DecidedAt = now;
        RejectReason = reason.Trim();
        MoveTo(BookingStatus.Rejected, userId, now, RejectReason);
        return BookingResult.Ok;
    }

    // ------------------------------------------------------------------ cancelling

    /// <summary>
    /// Requested or Approved → Cancelled, by the donor who booked, until the end of the day before
    /// the meal (Q1). After that only the institution can. Gives the place back. A regular day is not
    /// cancelled but let go (plan 08, K24): the skip records the day, a cancel would not.
    /// </summary>
    /// <param name="slotDate">The booking's slot-day.</param>
    /// <param name="today">Today in Sri Lanka (<see cref="SriLankaTime"/>).</param>
    public BookingResult CancelAsDonor(string? reason, Guid userId, DateOnly slotDate, DateOnly today, DateTime now)
    {
        if (DonorUserId != userId)
        {
            return BookingResult.Fail(BookingErrors.NotDonor);
        }

        if (Status is not (BookingStatus.Requested or BookingStatus.Approved))
        {
            return BookingResult.Fail(BookingErrors.InvalidTransition);
        }

        if (StandingSponsorshipId is not null)
        {
            return BookingResult.Fail(BookingErrors.RegularDay);
        }

        if (!DonorMayStillCancel(slotDate, today))
        {
            return BookingResult.Fail(BookingErrors.CancelWindowClosed);
        }

        if (reason?.Trim().Length > MaxReason)
        {
            return BookingResult.Fail(BookingErrors.ReasonTooLong);
        }

        Cancel(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), userId, now);
        return BookingResult.Ok;
    }

    /// <summary>
    /// Whether the donor's own booking in <paramref name="status"/> on <paramref name="slotDate"/>
    /// may still be cancelled by them: what <see cref="CancelAsDonor"/> allows, for the screen's
    /// cancel button.
    /// </summary>
    /// <param name="today">Today in Sri Lanka (<see cref="SriLankaTime"/>).</param>
    /// <param name="regular">It is a standing day, so it is let go instead.</param>
    public static bool DonorMayCancel(string status, DateOnly slotDate, DateOnly today, bool regular) =>
        !regular && status is BookingStatus.Requested or BookingStatus.Approved && DonorMayStillCancel(slotDate, today);

    /// <summary>Q1: until the end of the day before the meal.</summary>
    private static bool DonorMayStillCancel(DateOnly slotDate, DateOnly today) => today < slotDate;

    /// <summary>Requested or Approved → Cancelled, by the institution, at any time and with a reason. Gives the place back.</summary>
    public BookingResult CancelAsInstitution(string reason, Guid userId, DateTime now)
    {
        if (Status is not (BookingStatus.Requested or BookingStatus.Approved))
        {
            return BookingResult.Fail(BookingErrors.InvalidTransition);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return BookingResult.Fail(BookingErrors.ReasonRequired);
        }

        if (reason.Trim().Length > MaxReason)
        {
            return BookingResult.Fail(BookingErrors.ReasonTooLong);
        }

        Cancel(reason.Trim(), userId, now);
        return BookingResult.Ok;
    }

    // ------------------------------------------------------------------ after the meal

    /// <summary>Approved → Completed, on or after the slot-day. Keeps the place.</summary>
    public BookingResult Complete(Guid userId, DateOnly slotDate, DateOnly today, DateTime now)
    {
        var refused = RefuseUnlessClosable(slotDate, today);
        if (refused is not null)
        {
            return BookingResult.Fail(refused);
        }

        CompletedAt = now;
        MoveTo(BookingStatus.Completed, userId, now, comment: null);
        return BookingResult.Ok;
    }

    /// <summary>Approved → NoShow, on or after the slot-day. Keeps the place: it was promised, and taken.</summary>
    public BookingResult MarkNoShow(Guid userId, DateOnly slotDate, DateOnly today, DateTime now)
    {
        var refused = RefuseUnlessClosable(slotDate, today);
        if (refused is not null)
        {
            return BookingResult.Fail(refused);
        }

        MoveTo(BookingStatus.NoShow, userId, now, comment: null);
        return BookingResult.Ok;
    }

    // ------------------------------------------------------------------ plumbing

    private string? RefuseUnlessClosable(DateOnly slotDate, DateOnly today)
    {
        if (Status != BookingStatus.Approved)
        {
            return BookingErrors.InvalidTransition;
        }

        return today < slotDate ? BookingErrors.TooEarly : null;
    }

    private void Cancel(string? reason, Guid userId, DateTime now)
    {
        CancelledBy = userId;
        CancelledAt = now;
        CancelReason = reason;
        MoveTo(BookingStatus.Cancelled, userId, now, reason);
    }

    private void MoveTo(string status, Guid userId, DateTime now, string? comment)
    {
        StatusHistory.Add(new BookingStatusChange
        {
            BookingId = Id,
            FromStatus = Status,
            ToStatus = status,
            ChangedBy = userId,
            ChangedAt = now,
            Comment = comment,
        });

        Status = status;
    }
}
