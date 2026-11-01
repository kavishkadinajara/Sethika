namespace Sethika.Application.Abstractions;

/// <summary>What an appointment's text says, and the donor it goes to.</summary>
/// <param name="Language">The donor's <c>PreferredLanguage</c>, which picks the institution's name.</param>
/// <param name="StartsAt">UTC; the text gives it as a Colombo date and time.</param>
public sealed record AppointmentTextDetails(
    string AppointmentNo,
    string Status,
    Guid DonorUserId,
    string? Language,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    DateTime StartsAt);

/// <summary>A leader on an appointment, as a text reaches them (V14).</summary>
/// <param name="Phone">The roster's number, else the linked account's confirmed one; <see langword="null"/> when neither.</param>
/// <param name="Language">The linked account's language, else the roster's (default <c>si</c>).</param>
/// <param name="Response">Their answer: <c>Pending</c>, <c>Accepted</c>, <c>Declined</c> or <c>Removed</c>.</param>
public sealed record LeaderTextRecipient(int LeaderId, string? Phone, string Language, string Response);

/// <summary>
/// Dapper reads for the appointment texts (Phase E, 8.8). They run as the platform - the outbox
/// processor and the reminder job - so Row-Level Security shows every institution's rows.
/// </summary>
public interface IAppointmentNotificationQueries
{
    /// <summary>The appointment <paramref name="appointmentNo"/>, or <see langword="null"/> if there is none.</summary>
    Task<AppointmentTextDetails?> GetAsync(string appointmentNo, CancellationToken cancellationToken = default);

    /// <summary>Every leader ever assigned to the appointment, whatever their answer.</summary>
    Task<IReadOnlyList<LeaderTextRecipient>> LeadersAsync(string appointmentNo, CancellationToken cancellationToken = default);

    /// <summary>Approved appointments starting in <c>[fromUtc, toUtc)</c>, in the order they start.</summary>
    Task<IReadOnlyList<AppointmentTextDetails>> ListApprovedStartingAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
