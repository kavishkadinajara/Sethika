namespace Sethika.Application.Abstractions;

/// <summary>A business number series in <c>core.NumberSeries</c>: <c>REQ-2026-000123</c>.</summary>
/// <param name="Code">The series, <c>SeriesCode</c> in the table.</param>
/// <param name="Prefix">Written before the year.</param>
/// <param name="PadLength">Digits the number is zero-padded to.</param>
public sealed record NumberSeriesDefinition(string Code, string Prefix, byte PadLength)
{
    /// <summary>The scope of a series shared by the whole platform.</summary>
    public const string PlatformScope = "PLATFORM";

    /// <summary>Institution applications (plan 02, B5), one series for the whole platform.</summary>
    public static NumberSeriesDefinition RegistrationRequest { get; } = new("REQUEST", "REQ", 6);

    /// <summary>Approved institutions' <c>InstitutionCode</c> (plan 02, B5).</summary>
    public static NumberSeriesDefinition Institution { get; } = new("INSTITUTION", "INS", 6);

    /// <summary>Meal bookings' <c>BookingNo</c> (plan 03, C7), one series for the whole platform.</summary>
    public static NumberSeriesDefinition Booking { get; } = new("BOOKING", "BK", 6);

    /// <summary>Service appointments' <c>AppointmentNo</c> (Phase E), one series for the whole platform like bookings.</summary>
    public static NumberSeriesDefinition Appointment { get; } = new("APPOINTMENT", "APT", 6);

    /// <summary>Requests for a standing sponsor's day (plan 08, K6), one series for the whole platform.</summary>
    public static NumberSeriesDefinition DayRequest { get; } = new("DAYREQUEST", "DR", 6);
}

/// <summary>
/// Hands out the next number of a series, gap-free, inside the caller's transaction.
/// </summary>
/// <remarks>
/// The increment takes the series row's lock and holds it until the caller's transaction ends, so
/// two callers never get the same number, and a caller that rolls back gives its number back: the
/// next caller gets it. That is what gap-free means, and it is why a transaction is required - a
/// number taken outside one would be spent even if the work it numbered was never saved.
/// </remarks>
public interface INumberSeries
{
    /// <summary>The next number, formatted <c>{Prefix}-{year}-{number}</c>.</summary>
    /// <param name="series">Which series.</param>
    /// <param name="scopeKey"><c>PLATFORM</c>, or an institution's code for a per-institution series.</param>
    /// <param name="year">The UTC year the number belongs to; each year starts again at 1.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task<string> NextAsync(
        NumberSeriesDefinition series,
        string scopeKey,
        int year,
        CancellationToken cancellationToken = default);
}
