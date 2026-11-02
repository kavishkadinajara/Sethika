using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Memorial;

/// <summary>Where an observance stands for the donor.</summary>
public static class ObservanceState
{
    /// <summary>A live booking (requested or approved) for the plan falls on its day.</summary>
    public const string Booked = "booked";

    /// <summary>Today or later, with no booking yet.</summary>
    public const string ToBook = "toBook";

    /// <summary>Gone by with no booking.</summary>
    public const string Past = "past";
}

/// <summary>One observance of a plan, as the planner shows it.</summary>
/// <param name="Rule">The rule's code, which a booking for this observance sends back.</param>
/// <param name="Number">1 the first time; for a yearly observance, which year.</param>
/// <param name="State">An <see cref="ObservanceState"/>.</param>
/// <param name="EveryYear">A yearly observance whose day is booked every year by a yearly booking of the plan's.</param>
public sealed record MemorialObservanceView(
    string Rule,
    string Name,
    string? NameSi,
    string? NameTa,
    bool RepeatsAnnually,
    int Number,
    DateOnly Date,
    string State,
    string? BookingNo,
    string? Institution,
    string? InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    bool EveryYear);

/// <summary>A memorial plan with its observances, in date order.</summary>
/// <param name="Religion">The <c>RELIGION</c> lookup code.</param>
/// <param name="Relationship">The <c>RELATIONSHIP</c> lookup code, if one was given.</param>
public sealed record MemorialPlanView(
    Guid PublicId,
    string DeceasedName,
    string? Relationship,
    DateOnly DateOfPassing,
    string Religion,
    string? Notes,
    IReadOnlyList<MemorialObservanceView> Observances);

/// <summary>The donor's memorial plans and the days they fall on (plan 07, H6).</summary>
/// <remarks>
/// The days come from <see cref="MemorialObservances"/> and the active rules at the moment of
/// reading, never from stored dates: a rule corrected in the seed moves every plan. An observance is
/// booked when one of the plan's live bookings is on its day, which is the only day a booking for an
/// observance may be made (<c>BookMealHandler</c>), or the day the yearly job books for it.
/// </remarks>
public sealed class MemorialPlanner(IMemorialQueries queries, TimeProvider timeProvider)
{
    /// <summary>The donor's active plans, the newest first.</summary>
    public async Task<IReadOnlyList<MemorialPlanView>> ListAsync(Guid donorId, CancellationToken cancellationToken = default)
    {
        var data = await queries.LoadAsync(donorId, null, cancellationToken).ConfigureAwait(false);
        return [.. data.Plans.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Select(p => View(p, data))];
    }

    /// <summary>One active plan of the donor's, or <see langword="null"/>.</summary>
    public async Task<MemorialPlanView?> GetAsync(Guid publicId, Guid donorId, CancellationToken cancellationToken = default)
    {
        var data = await queries.LoadAsync(donorId, publicId, cancellationToken).ConfigureAwait(false);
        return data.Plans.SingleOrDefault() is { } plan ? View(plan, data) : null;
    }

    private MemorialPlanView View(MemorialPlanRow plan, MemorialPlansData data)
    {
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        var rules = data.Rules.Where(r => r.ReligionLookupId == plan.ReligionLookupId);

        var observances = MemorialObservances.For(plan.DateOfPassing, rules, today)
            .Select(o =>
            {
                var booking = data.Bookings.FirstOrDefault(b => b.MemorialPlanId == plan.Id && b.Date == o.Date);
                var state = booking is not null ? ObservanceState.Booked
                    : o.Date < today ? ObservanceState.Past
                    : ObservanceState.ToBook;
                var everyYear = o.Rule.RepeatsAnnually
                                && data.Yearly.Any(y => y.MemorialPlanId == plan.Id && y.Month == o.Date.Month && y.Day == o.Date.Day);

                return new MemorialObservanceView(
                    o.Rule.Code, o.Rule.Name, o.Rule.NameSi, o.Rule.NameTa, o.Rule.RepeatsAnnually, o.Number, o.Date, state,
                    booking?.BookingNo, booking?.Institution, booking?.InstitutionName, booking?.InstitutionNameSi, booking?.InstitutionNameTa,
                    everyYear);
            })
            .ToList();

        return new MemorialPlanView(plan.PublicId, plan.DeceasedName, plan.Relationship, plan.DateOfPassing, plan.Religion, plan.Notes, observances);
    }
}
