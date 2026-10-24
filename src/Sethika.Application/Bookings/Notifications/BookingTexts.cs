using System.Globalization;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Bookings.Notifications;

/// <summary>The values every booking text opens with (§8, E2): <c>{bookingNo}: {institution}, {date} {time}</c>.</summary>
internal static class BookingTexts
{
    /// <summary>
    /// The placeholders for <paramref name="booking"/>. The institution's name is in the donor's
    /// language when it has one there; the slot's name is never sent (E2).
    /// </summary>
    /// <remarks>The date is <c>yyyy-MM-dd</c>: the same in all three languages, and never read as month-first.</remarks>
    public static IReadOnlyDictionary<string, string> Values(BookingTextDetails booking) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bookingNo"] = booking.BookingNo,
            ["institution"] = InstitutionName(booking),
            ["date"] = booking.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = booking.StartTime,
        };

    private static string InstitutionName(BookingTextDetails booking) =>
        (booking.Language switch
        {
            "si" => booking.InstitutionNameSi,
            "ta" => booking.InstitutionNameTa,
            _ => null,
        }) is { Length: > 0 } name
            ? name
            : booking.InstitutionName;
}
