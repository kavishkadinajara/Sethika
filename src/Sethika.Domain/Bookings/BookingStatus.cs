namespace Sethika.Domain.Bookings;

/// <summary>
/// <c>booking.Bookings.Status</c>, as <c>CK_Bookings_Status</c> spells it (plan 03, C5).
/// </summary>
/// <remarks>
/// <code>
///            ┌─approve─▶ Approved ─complete─▶ Completed
/// Requested ─┤              │      └─no-show─▶ NoShow
///            └─reject──▶ Rejected
/// Requested / Approved ─cancel─▶ Cancelled
/// </code>
/// A slot that needs no approval starts the booking at <see cref="Approved"/>.
/// </remarks>
public static class BookingStatus
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
    public const string NoShow = "NoShow";

    /// <summary>
    /// Whether a booking in <paramref name="status"/> counts in its slot-day's
    /// <c>BookedSponsors</c> (C1): a request holds its place, and so does a meal that happened or
    /// was missed. Only a rejection or a cancellation gives the place back.
    /// </summary>
    public static bool HoldsPlace(string status) => status is not (Rejected or Cancelled);
}

/// <summary><c>booking.MealSlots.BookingMode</c>.</summary>
public static class BookingMode
{
    /// <summary>One sponsor takes the whole slot-day.</summary>
    public const string Exclusive = "Exclusive";

    /// <summary>Up to <c>MaxSponsors</c> (2-20) share it.</summary>
    public const string Shared = "Shared";
}

/// <summary><c>booking.Bookings.FulfilmentMode</c>: who provides the meal.</summary>
public static class FulfilmentMode
{
    public const string DonorBrings = "DonorBrings";
    public const string InstitutionPrepares = "InstitutionPrepares";

    /// <summary>In the schema, cut from plan 03 (§4): no slot offers it yet.</summary>
    public const string Caterer = "Caterer";
}

/// <summary>
/// Why a booking was refused. Each is also the API's error code, so the screen can say which rule
/// stopped it.
/// </summary>
public static class BookingErrors
{
    // -------------------------------------------------- bookability (C4), in the order they are checked

    public const string SlotInactive = "slot_inactive";
    public const string InstitutionNotActive = "institution_not_active";
    public const string NotOnThisDay = "not_on_this_day";
    public const string OutOfSeason = "out_of_season";
    public const string DateBlocked = "date_blocked";
    public const string TooSoon = "too_soon";
    public const string TooFar = "too_far";
    public const string HeadcountOutOfRange = "headcount_out_of_range";
    public const string FulfilmentNotOffered = "fulfilment_not_offered";

    // -------------------------------------------------- the engine (C2, C6)

    /// <summary>No place left on the slot-day, by sponsors or by headcount.</summary>
    public const string SlotFull = "slot_full";

    /// <summary>The donor already holds an active booking on this slot-day.</summary>
    public const string AlreadyBooked = "already_booked";

    /// <summary>
    /// The availability calendar's reason for a day full only through standing days (plan 08, K6): a
    /// regular donor holds it, and a donor may ask the institution for it.
    /// </summary>
    public const string Held = "held";

    // -------------------------------------------------- transitions (C5)

    /// <summary>Not allowed from the booking's current status.</summary>
    public const string InvalidTransition = "invalid_transition";

    /// <summary>Only the donor who made the booking may cancel it as a donor.</summary>
    public const string NotDonor = "not_donor";

    /// <summary>A donor may cancel only until the end of the day before the meal (Q1).</summary>
    public const string CancelWindowClosed = "cancel_window_closed";

    /// <summary>A regular day is let go through the skip, which records it, never cancelled (plan 08, K24).</summary>
    public const string RegularDay = "regular_day";

    /// <summary>A meal can be marked completed or missed only on or after its day.</summary>
    public const string TooEarly = "too_early";

    /// <summary>Rejecting, or cancelling as the institution, says why.</summary>
    public const string ReasonRequired = "reason_required";

    /// <summary>The reason is longer than <see cref="Booking.MaxReason"/>.</summary>
    public const string ReasonTooLong = "reason_too_long";
}

/// <summary>The outcome of asking the aggregate for a change: done, or the reason it was refused.</summary>
public readonly record struct BookingResult(string? Error)
{
    public bool Succeeded => Error is null;

    public static BookingResult Ok { get; }

    public static BookingResult Fail(string error) => new(error);
}
