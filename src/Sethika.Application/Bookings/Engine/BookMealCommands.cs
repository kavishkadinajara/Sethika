namespace Sethika.Application.Bookings.Engine;

/// <summary>A donor's booking of a slot-day, as the booking form sends it.</summary>
/// <param name="MealSlot">The slot's <c>PublicId</c>.</param>
/// <param name="Date">The slot-day, a Sri Lankan date.</param>
/// <param name="Fulfilment">A <c>FulfilmentMode</c>: who provides the meal.</param>
/// <param name="EveryYear">"Book this every year" (plan 07, H5): the same meal on the same day from next year on.</param>
public sealed record BookMealCommand(
    Guid MealSlot,
    DateOnly Date,
    short Headcount,
    string? Fulfilment,
    string? InMemoryOf,
    string? Notes,
    bool EveryYear = false);

/// <summary>What became of one year of a yearly booking (plan 07, H3, H4).</summary>
public enum RecurringYearOutcome
{
    /// <summary>The year's booking was made.</summary>
    Booked,

    /// <summary>The engine refused the day; the donor is texted why.</summary>
    NotBooked,

    /// <summary>Another run tried this year first, or the donor stopped the yearly booking.</summary>
    AlreadyTried,

    /// <summary>The donor already holds the day, booked themselves.</summary>
    AlreadyHeld,

    /// <summary>The day had passed before the job reached it.</summary>
    Past,

    /// <summary>The donor has no confirmed phone, which a booking and its texts need.</summary>
    NoPhone,

    /// <summary>Another booking of the slot-day held the lock; nothing was claimed, and the next run tries again.</summary>
    Busy,
}

/// <summary>One year of a yearly booking: its outcome, the engine's refusal, and the booking made.</summary>
/// <param name="Error">A <c>BookingErrors</c> code when <see cref="RecurringYearOutcome.NotBooked"/>.</param>
public sealed record RecurringYearResult(RecurringYearOutcome Outcome, string? Error, string? BookingNo)
{
    internal static RecurringYearResult Of(RecurringYearOutcome outcome) => new(outcome, null, null);
}

/// <summary>The outcome of a booking attempt: the booking's number and status, or why not.</summary>
/// <param name="Error">An API error code (<c>BookingErrors</c> or one of the constants here); <see langword="null"/> on success.</param>
public sealed record BookMealResult(string? Error, IReadOnlyList<string> Fields, string? BookingNo, string? Status)
{
    /// <summary>No active slot with that id.</summary>
    public const string SlotNotFound = "slot_not_found";

    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>Another booking of the same slot-day held the lock for the whole wait; try again.</summary>
    public const string SlotBusy = "slot_busy";

    /// <summary>The signed-in account has no confirmed phone, which a booking's texts go to.</summary>
    public const string PhoneRequired = "phone_required";

    public bool Succeeded => Error is null;

    internal static BookMealResult Ok(string bookingNo, string status) => new(null, [], bookingNo, status);

    internal static BookMealResult Failed(string error) => new(error, [], null, null);

    internal static BookMealResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null);
}
