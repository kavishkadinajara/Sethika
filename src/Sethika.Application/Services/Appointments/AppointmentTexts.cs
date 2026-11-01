using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Appointments;

/// <summary>
/// Sends an appointment's texts (Phase E, 8.8, V14) to the donor or to a leader. Every text opens
/// with <c>Sethika {appointmentNo}: {institution}, {date} {time}.</c>, like the booking texts.
/// </summary>
/// <remarks>
/// The service's name is never sent: the institution types it freely, and free text from the
/// Sethika sender id could be made to read as Sethika's own (the booking texts' E2). Nor is a reason:
/// "sign in to see why" instead. The date and time are Colombo's.
/// </remarks>
public sealed class AppointmentTexts(IUserTextMessages texts)
{
    public const string Requested = "APPOINTMENT_REQUESTED";
    public const string Approved = "APPOINTMENT_APPROVED";
    public const string Rejected = "APPOINTMENT_REJECTED";
    public const string Cancelled = "APPOINTMENT_CANCELLED";
    public const string Reminder = "APPOINTMENT_REMINDER";
    public const string LeaderAssigned = "LEADER_ASSIGNED";
    public const string LeaderRemoved = "LEADER_REMOVED";
    public const string LeaderCancelled = "LEADER_CANCELLED";
    public const string LeaderReminder = "LEADER_REMINDER";

    /// <summary>To the donor, in their language, at their confirmed phone.</summary>
    public Task<UserTextOutcome> ToDonorAsync(AppointmentTextDetails appointment, string template, string dedupKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return texts.SendAsync(appointment.DonorUserId, template, Values(appointment, appointment.Language), dedupKey, cancellationToken);
    }

    /// <summary>To a leader, in their language; <see langword="false"/> when they have no phone to text.</summary>
    public async Task<bool> ToLeaderAsync(AppointmentTextDetails appointment, LeaderTextRecipient leader, string template, string dedupKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(leader);

        if (string.IsNullOrWhiteSpace(leader.Phone))
        {
            return false;
        }

        await texts.SendToPhoneAsync(leader.Phone, leader.Language, template, Values(appointment, leader.Language), dedupKey, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Whether a leader with this answer is still expected, and so hears of changes and reminders.</summary>
    public static bool Expects(LeaderTextRecipient leader) => AssignmentResponse.IsLive(leader?.Response ?? string.Empty);

    /// <summary>
    /// The placeholders. The institution's name is in the reader's language when it has one there.
    /// </summary>
    /// <remarks>The date is <c>yyyy-MM-dd</c>: the same in all three languages, and never read as month-first.</remarks>
    internal static IReadOnlyDictionary<string, string> Values(AppointmentTextDetails appointment, string? language)
    {
        var colombo = SriLankaTime.FromUtc(appointment.StartsAt);
        var name = (language switch
        {
            "si" => appointment.InstitutionNameSi,
            "ta" => appointment.InstitutionNameTa,
            _ => null,
        }) is { Length: > 0 } local ? local : appointment.InstitutionName;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["appointmentNo"] = appointment.AppointmentNo,
            ["institution"] = name,
            ["date"] = colombo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = colombo.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
    }
}
