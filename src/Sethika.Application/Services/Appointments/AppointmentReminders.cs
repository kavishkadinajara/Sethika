using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Services.Appointments;

/// <summary>One reminder run: the day it covered, the appointments on it, and the texts sent.</summary>
/// <param name="NotSent">Donors without a confirmed phone and leaders without any phone.</param>
public sealed record AppointmentReminderRun(DateOnly Date, int Appointments, int Sent, int NotSent);

/// <summary>
/// The evening reminder for religious services (V15): every approved appointment that starts
/// tomorrow in Colombo, texted to its donor and to every leader still expected, once each.
/// </summary>
/// <remarks>
/// "Tomorrow" is the Colombo day, from its midnight to the next, which in UTC runs from 18:30 the day
/// before. Only <c>Approved</c> appointments: a request nobody answered is not a service to remind
/// anyone of. The dedup key <c>appt-reminder:{AppointmentNo}:{recipient}</c> makes a run safe to repeat,
/// and a failed send stops the run so the retry carries on from there, like the meal reminder.
/// </remarks>
public sealed class AppointmentReminders(IAppointmentNotificationQueries queries, AppointmentTexts texts, TimeProvider timeProvider)
{
    /// <summary>The key a reminder to <paramref name="recipient"/> is sent under, at most once.</summary>
    /// <param name="recipient"><c>donor</c>, or <c>leader-{id}</c>.</param>
    public static string DedupKey(string appointmentNo, string recipient) => $"appt-reminder:{appointmentNo}:{recipient}";

    public async Task<AppointmentReminderRun> SendForTomorrowAsync(CancellationToken cancellationToken = default)
    {
        var tomorrow = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
        var appointments = await queries
            .ListApprovedStartingAsync(SriLankaTime.ToUtc(tomorrow, TimeOnly.MinValue), SriLankaTime.ToUtc(tomorrow.AddDays(1), TimeOnly.MinValue), cancellationToken)
            .ConfigureAwait(false);

        int sent = 0, notSent = 0;
        foreach (var appointment in appointments)
        {
            var donor = await texts
                .ToDonorAsync(appointment, AppointmentTexts.Reminder, DedupKey(appointment.AppointmentNo, "donor"), cancellationToken)
                .ConfigureAwait(false);
            Count(donor == UserTextOutcome.Sent);

            foreach (var leader in (await queries.LeadersAsync(appointment.AppointmentNo, cancellationToken).ConfigureAwait(false)).Where(AppointmentTexts.Expects))
            {
                Count(await texts
                    .ToLeaderAsync(appointment, leader, AppointmentTexts.LeaderReminder, DedupKey(appointment.AppointmentNo, $"leader-{leader.LeaderId}"), cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        return new AppointmentReminderRun(tomorrow, appointments.Count, sent, notSent);

        void Count(bool wasSent)
        {
            if (wasSent)
            {
                sent++;
            }
            else
            {
                notSent++;
            }
        }
    }
}
