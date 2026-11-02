using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Memorial;

/// <summary>One memorial reminder run: the day it ran, the observances due a text, and how many were texted.</summary>
/// <param name="NoPhone">Donors with no confirmed phone; nothing was sent to them.</param>
public sealed record MemorialReminderRun(DateOnly Today, int Due, int Sent, int NoPhone);

/// <summary>
/// The memorial reminder (plan 07, H8): one text to the donor for each observance in their active
/// plans that falls in the coming week and has no booking yet.
/// </summary>
/// <remarks>
/// The week is tomorrow to <see cref="DaysAhead"/> days on, not the seventh day alone, so a plan made
/// three days before an observance, or a night the job did not run, still gets its text; the dedup
/// key <c>memorial:{plan}:{rule}:{date}</c> makes it one text per observance however many nights see
/// it. The name of the person who died is never in the text: the donor typed it, and a template's
/// values are never typed text (<see cref="IUserTextMessages"/>). Runs as the platform.
/// </remarks>
public sealed class MemorialReminders(IMemorialQueries queries, IUserTextMessages texts, TimeProvider timeProvider)
{
    public const string Template = "MEMORIAL_REMINDER";

    /// <summary>How far ahead an observance is texted about: a week, to find a place and book it.</summary>
    public const int DaysAhead = 7;

    /// <summary>The key an observance's reminder is sent under, at most once.</summary>
    public static string DedupKey(Guid plan, string rule, DateOnly date) =>
        $"memorial:{plan}:{rule}:{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public async Task<MemorialReminderRun> SendAsync(CancellationToken cancellationToken = default)
    {
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        var from = today.AddDays(1);
        var to = today.AddDays(DaysAhead);

        var data = await queries.LoadAllAsync(from, cancellationToken).ConfigureAwait(false);
        var booked = data.Bookings.Select(b => (b.MemorialPlanId, b.Date)).ToHashSet();
        var rules = data.Rules.ToLookup(r => r.ReligionLookupId);

        var due = 0;
        var sent = 0;
        var noPhone = 0;
        foreach (var plan in data.Plans.OrderBy(p => p.Id))
        {
            var coming = MemorialObservances.For(plan.DateOfPassing, rules[plan.ReligionLookupId], from)
                .Where(o => o.Date >= from && o.Date <= to && !booked.Contains((plan.Id, o.Date)));

            foreach (var observance in coming)
            {
                due++;
                var outcome = await texts
                    .SendAsync(plan.DonorUserId, Template, Values(plan, observance), DedupKey(plan.PublicId, observance.Rule.Code, observance.Date), cancellationToken)
                    .ConfigureAwait(false);

                if (outcome == UserTextOutcome.Sent)
                {
                    sent++;
                }
                else
                {
                    noPhone++;
                }
            }
        }

        return new MemorialReminderRun(today, due, sent, noPhone);
    }

    /// <summary>The date, and the observance's name in the donor's language where the rule has one.</summary>
    private static Dictionary<string, string> Values(MemorialPlanRow plan, MemorialObservance observance) =>
        new(StringComparer.Ordinal)
        {
            ["date"] = observance.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["observance"] = (plan.Language switch
            {
                "si" => observance.Rule.NameSi,
                "ta" => observance.Rule.NameTa,
                _ => null,
            }) is { Length: > 0 } name
                ? name
                : observance.Rule.Name,
        };
}
