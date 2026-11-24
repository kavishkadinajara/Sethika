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

/// <summary>Whom a standing sponsorship's texts reach (plan 08, K10), and the line they open with.</summary>
/// <param name="DonorUserId">The donor book's linked account (K14), texted as any account is.</param>
/// <param name="Phone">The number the institution gave, E.164.</param>
/// <param name="DonorLanguage">The donor book's language, for a donor without an account.</param>
/// <param name="TextConsent">Whether staff ticked "this person agreed to receive texts" (Q2).</param>
/// <param name="AccountLanguage">The linked account's <c>PreferredLanguage</c>.</param>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
public sealed record StandingTextDetails(
    Guid? DonorUserId,
    string Phone,
    string DonorLanguage,
    bool TextConsent,
    string? AccountLanguage,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string StartTime)
{
    /// <summary>The language the text goes out in: the account's, or the donor book's.</summary>
    public string Language => DonorUserId is null ? DonorLanguage : AccountLanguage ?? DonorLanguage;
}

/// <summary>A day request's text (plan 08, K10): to the donor who asked, always an account.</summary>
/// <param name="BookingNo">Given: the asker's booking.</param>
public sealed record DayRequestTextDetails(
    string RequestNo,
    string? BookingNo,
    Guid DonorUserId,
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

    /// <summary>The standing sponsorship <paramref name="publicId"/>'s recipient (plan 08, K10), or <see langword="null"/>.</summary>
    Task<StandingTextDetails?> GetStandingAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>The day request <paramref name="requestNo"/>'s text (plan 08, K10), or <see langword="null"/>.</summary>
    Task<DayRequestTextDetails?> GetDayRequestAsync(string requestNo, CancellationToken cancellationToken = default);
}
