namespace Sethika.Domain.Bookings;

/// <summary>
/// A day let go from a standing sponsorship, or moved (plan 08, K4): a row of <c>booking.StandingExceptions</c>,
/// named for what it is rather than the table (a type ending in "Exception" reads as a thrown one).
/// </summary>
/// <remarks>
/// The nightly job never books a day that has one, so a standing day once let go stays let go. One per
/// sponsorship and day (<c>UQ_StandingExc_Day</c>).
/// </remarks>
public class StandingDayChange
{
    public long Id { get; set; }
    public int InstitutionId { get; set; }
    public int StandingSponsorshipId { get; set; }
    public DateOnly ExceptionDate { get; set; }

    /// <summary>One of the kinds below.</summary>
    public string Kind { get; set; } = Skipped;

    /// <summary>A swap's other day.</summary>
    public DateOnly? OtherDate { get; set; }

    /// <summary>The booking the change cancelled; a swap's <see cref="MovedFrom"/>, the one it made.</summary>
    public long? BookingId { get; set; }

    /// <summary>A <see cref="Released"/> day's request.</summary>
    public int? DayRequestId { get; set; }

    public string? Reason { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The sponsor cannot give that day (K5).</summary>
    public const string Skipped = "Skipped";

    /// <summary>Given to a donor who asked for it (K6).</summary>
    public const string Released = "Released";

    /// <summary>The institution has another use for the day (K7).</summary>
    public const string Blocked = "Blocked";

    /// <summary>A swap: this sponsor's day went to <see cref="OtherDate"/> (K8).</summary>
    public const string MovedTo = "MovedTo";

    /// <summary>A swap: this sponsor gives on this day instead of <see cref="OtherDate"/>.</summary>
    public const string MovedFrom = "MovedFrom";

    /// <summary>The longest reason: <c>Reason</c> is <c>NVARCHAR(300)</c>.</summary>
    public const int MaxReason = 300;
}
