namespace Sethika.Application.Bookings.Engine;

/// <summary>A donor's booking of a slot-day, as the booking form sends it.</summary>
/// <param name="MealSlot">The slot's <c>PublicId</c>.</param>
/// <param name="Date">The slot-day, a Sri Lankan date.</param>
/// <param name="Fulfilment">A <c>FulfilmentMode</c>: who provides the meal.</param>
public sealed record BookMealCommand(
    Guid MealSlot,
    DateOnly Date,
    short Headcount,
    string? Fulfilment,
    string? InMemoryOf,
    string? Notes);

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
