using Sethika.Application.Bookings.Engine;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>A new standing sponsorship, as the sponsorship form sends it (plan 08, K2).</summary>
/// <param name="Donor">The donor book entry's <c>PublicId</c>.</param>
/// <param name="MealSlot">The slot's <c>PublicId</c>.</param>
/// <param name="Pattern">A <c>StandingPattern</c>. Only that pattern's own fields may be sent.</param>
/// <param name="MonthInterval">Monthly: every this many months from the start month (1-12, default 1).</param>
/// <param name="WeekDays">Weekly: 0 Sunday ... 6 Saturday.</param>
/// <param name="MonthDays">Monthly: 1 ... 31. A day a month does not have is skipped there (Q3).</param>
/// <param name="YearMonth">Yearly: the month.</param>
/// <param name="YearDay">Yearly: the day; 29 February is skipped in common years.</param>
/// <param name="StartDate">The first day it may fall on: today or later. A <c>Once</c>'s day.</param>
/// <param name="EndDate">The last day it may fall on; none for no end. A <c>Once</c> needs none.</param>
public sealed record SponsorshipInput(
    Guid? Donor,
    Guid? MealSlot,
    string? Pattern,
    int? MonthInterval,
    IReadOnlyList<int>? WeekDays,
    IReadOnlyList<int>? MonthDays,
    int? YearMonth,
    int? YearDay,
    DateOnly? StartDate,
    DateOnly? EndDate,
    short? Headcount,
    string? Fulfilment,
    string? InMemoryOf,
    string? Notes);

/// <summary>The outcome of setting up, ending or skipping a day of a standing sponsorship.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="Version">The sponsorship's <c>RowVersion</c> after the change, for the next one.</param>
/// <param name="Days">
/// Setting one up: each day of the next month of its slot's booking window and what became of it -
/// booked, or why not (K9). The days are kept either way: a monthly pattern that meets one full day is
/// not refused.
/// </param>
/// <param name="Later">Setting one up: the window's days after <see cref="Days"/>, left to the standing job.</param>
public sealed record SponsorshipChangeResult(
    string? Error,
    IReadOnlyList<string> Fields,
    Guid? PublicId,
    byte[]? Version,
    IReadOnlyList<StandingDayResult> Days,
    int Later = 0)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No sponsorship with that id in the caller's institution.</summary>
    public const string NotFound = "sponsorship_not_found";

    /// <summary>The sponsorship has ended; nothing more can change on it.</summary>
    public const string Ended = "sponsorship_ended";

    /// <summary>The day was already let go or moved (<c>UQ_StandingExc_Day</c>).</summary>
    public const string DayAlreadyChanged = "day_already_changed";

    /// <summary>Someone changed it since it was read; read it again.</summary>
    public const string Stale = "stale";

    public bool Succeeded => Error is null;

    internal static SponsorshipChangeResult For(Guid publicId, byte[] version, IReadOnlyList<StandingDayResult> days, int later = 0) => new(null, [], publicId, version, days, later);

    internal static SponsorshipChangeResult Done() => new(null, [], null, null, []);

    internal static SponsorshipChangeResult Failed(string error) => new(error, [], null, null, []);

    internal static SponsorshipChangeResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null, []);
}
