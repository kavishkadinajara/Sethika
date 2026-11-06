using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Discovery;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>One of a sponsorship's next days as the form shows it before saving (plan 08, K9).</summary>
/// <param name="State">
/// <c>free</c>: the engine would book it now; <c>clash</c>: it would not, and <paramref name="Reason"/>
/// (a <see cref="BookingErrors"/> code) says why; <c>later</c>: beyond the slot's booking window, booked
/// when the window reaches it.
/// </param>
public sealed record PreviewDay(DateOnly Date, string State, string? Reason);

/// <summary>The outcome of a preview: the next days, or the fields to fix first.</summary>
public sealed record SponsorshipPreviewResult(string? Error, IReadOnlyList<string> Fields, IReadOnlyList<PreviewDay> Days)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// The next days a sponsorship would have, and any that clash, before it is saved (plan 08, K9): the
/// form's preview while staff choose the pattern.
/// </summary>
/// <remarks>
/// Only the meal slot and the pattern are read: the donor and the meal's details do not move a day.
/// The pattern is checked by <see cref="StandingSponsorshipHandler"/>'s own rules and laid out by the
/// arithmetic the nightly job books by; each day in the window is judged by
/// <see cref="AvailabilityCalendar"/>, the rule donors' calendars use. Nothing is written or locked, so a
/// day can still be taken before staff save: saving answers with what became of each day.
/// </remarks>
public sealed class StandingSponsorshipPreview(ISethikaDbContext db, AvailabilityCalendar calendar, TimeProvider timeProvider)
{
    public const string Free = "free";
    public const string Clash = "clash";
    public const string Later = "later";

    public async Task<SponsorshipPreviewResult> PreviewAsync(int institutionId, SponsorshipInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);

        var slot = input.MealSlot is { } slotId
            ? await db.MealSlots
                .AsNoTracking()
                .Where(s => s.PublicId == slotId && s.InstitutionId == institutionId && s.IsActive)
                .Select(s => new { s.PublicId, s.MaxAdvanceDays })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var invalid = new List<string>();
        if (slot is null)
        {
            invalid.Add("mealSlot");
        }

        var sponsorship = new StandingSponsorship();
        StandingSponsorshipHandler.ApplyPattern(input, sponsorship, today, invalid);
        if (invalid.Count > 0)
        {
            return new SponsorshipPreviewResult(SponsorshipChangeResult.InvalidDetails, invalid, []);
        }

        var next = sponsorship.NextDays(sponsorship.StartDate, StandingSponsorshipReader.NextDayCount);
        var windowEnd = today.AddDays(slot!.MaxAdvanceDays);
        var inWindow = next.Where(d => d <= windowEnd).ToList();

        var slug = await db.Institutions
            .Where(i => i.Id == institutionId)
            .Select(i => i.Slug)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        // No active institution (suspended, say): the engine refuses every day for that reason.
        var judged = (await calendar.DaysAsync(slug, slot.PublicId, inWindow, cancellationToken).ConfigureAwait(false))
            ?.ToDictionary(d => d.Date);

        var days = next
            .Select(date =>
            {
                if (date > windowEnd)
                {
                    return new PreviewDay(date, Later, null);
                }

                var day = judged?.GetValueOrDefault(date);
                return day is { Bookable: true }
                    ? new PreviewDay(date, Free, null)
                    : new PreviewDay(date, Clash, day?.Reason ?? BookingErrors.InstitutionNotActive);
            })
            .ToList();

        return new SponsorshipPreviewResult(null, [], days);
    }
}
