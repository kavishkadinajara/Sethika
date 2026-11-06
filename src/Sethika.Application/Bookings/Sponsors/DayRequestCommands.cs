namespace Sethika.Application.Bookings.Sponsors;

/// <summary>A donor asks for a day a regular donor holds (plan 08, K6).</summary>
/// <param name="MealSlot">The slot's <c>PublicId</c>.</param>
/// <param name="Fulfilment">How they would give the meal: a <c>FulfilmentMode</c> the slot offers.</param>
public sealed record DayRequestInput(Guid? MealSlot, DateOnly? Date, short? Headcount, string? Fulfilment, string? Note);

/// <summary>The outcome of asking for a day, or of the institution's answer.</summary>
/// <param name="Error">An API error code (these, or a <c>BookingErrors</c> code); <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="RequestNo">The request asked for or answered.</param>
/// <param name="BookingNo">Given: the asker's booking.</param>
public sealed record DayRequestResult(string? Error, IReadOnlyList<string> Fields, string? RequestNo, string? BookingNo)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No such institution, slot, or request that the caller may act on.</summary>
    public const string NotFound = "day_request_not_found";

    /// <summary>The day is not held by a regular donor: book it, or it cannot be had at all.</summary>
    public const string NotHeld = "day_not_held";

    /// <summary>The donor already has an open request for this slot-day (<c>UX_DayRequests_OpenPerDonor</c>).</summary>
    public const string AlreadyAsked = "day_request_open";

    /// <summary>The request was already given, declined or withdrawn.</summary>
    public const string Answered = "day_request_answered";

    /// <summary>Asking needs a confirmed phone: the booking it may become needs one.</summary>
    public const string PhoneRequired = "phone_required";

    /// <summary>Someone else answered or changed it first; read it again.</summary>
    public const string Stale = "stale";

    public bool Succeeded => Error is null;

    internal static DayRequestResult Ok(string requestNo, string? bookingNo = null) => new(null, [], requestNo, bookingNo);

    internal static DayRequestResult Failed(string error) => new(error, [], null, null);

    internal static DayRequestResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null);
}
