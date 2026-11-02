namespace Sethika.Application.Abstractions;

/// <summary>What a booking's text says, and who it goes to.</summary>
/// <param name="DonorUserId">The donor's account; <see langword="null"/> for a booking made without one.</param>
/// <param name="Language">The donor's <c>PreferredLanguage</c>, which picks the institution's name.</param>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
public sealed record BookingTextDetails(
    string BookingNo,
    string Status,
    Guid? DonorUserId,
    string? Language,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    DateOnly Date,
    string StartTime);

/// <summary>
/// Dapper reads for the booking texts (plan 03, 5.6). They run as the platform - the outbox processor
/// and the reminder job - so Row-Level Security shows every institution's bookings.
/// </summary>
public interface IBookingNotificationQueries
{
    /// <summary>The booking <paramref name="bookingNo"/>, or <see langword="null"/> if there is none.</summary>
    Task<BookingTextDetails?> GetAsync(string bookingNo, CancellationToken cancellationToken = default);

    /// <summary>Approved bookings with a donor account on <paramref name="slotDate"/>, in the order they were made.</summary>
    Task<IReadOnlyList<BookingTextDetails>> ListApprovedOnAsync(DateOnly slotDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The people who joined approved bookings on <paramref name="slotDate"/> (plan 07, H19), one row each,
    /// in the order they joined: <see cref="BookingTextDetails.DonorUserId"/> is the co-sponsor's account.
    /// </summary>
    Task<IReadOnlyList<BookingTextDetails>> ListJoinedOnAsync(DateOnly slotDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The yearly booking <paramref name="publicId"/>'s text for <paramref name="slotDate"/> (plan 07, H4),
    /// with no booking number; <see langword="null"/> if there is none.
    /// </summary>
    Task<BookingTextDetails?> GetRecurringAsync(Guid publicId, DateOnly slotDate, CancellationToken cancellationToken = default);
}
