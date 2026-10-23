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

    /// <summary>Always <c>Online</c> in plan 03; staff booking for a donor is cut (§4).</summary>
    public string Source { get; set; } = OnlineSource;

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

        DecidedBy = userId;
        DecidedAt = now;
        RejectReason = reason.Trim();
        MoveTo(BookingStatus.Rejected, userId, now, RejectReason);
        return BookingResult.Ok;
    }

    // ------------------------------------------------------------------ cancelling

    /// <summary>
    /// Requested or Approved → Cancelled, by the donor who booked, until the end of the day before
    /// the meal (Q1). After that only the institution can. Gives the place back.
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

        if (today >= slotDate)
        {
            return BookingResult.Fail(BookingErrors.CancelWindowClosed);
        }

        Cancel(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), userId, now);
        return BookingResult.Ok;
    }

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
