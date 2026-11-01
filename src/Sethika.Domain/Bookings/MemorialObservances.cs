namespace Sethika.Domain.Bookings;

/// <summary>A day a memorial plan's observance falls on.</summary>
/// <param name="Number">1 the first time it is kept; for a yearly observance, which year it is.</param>
public sealed record MemorialObservance(MemorialRule Rule, int Number, DateOnly Date);

/// <summary>
/// The Memorial Dana Planner's arithmetic (plan 07, H6): the days a death is remembered on, from the
/// date of passing and the religion's <see cref="MemorialRule"/>s. Pure, so the rules are tested
/// without a database and a new rule row needs no code.
/// </summary>
/// <remarks>
/// An observance falls on the date of passing plus the rule's months, then plus its days. Months come
/// first because "three months" means the same day of the month three months on, and a month end
/// stays inside its month: a death on 30 November 2026 is remembered at three months on 28 February
/// 2027, and one a year later on 29 February 2028. A yearly observance's later years are counted from the date of passing again,
/// not from its first date, so a death on 29 February is remembered on 29 February in a leap year and
/// on 28 February in the others.
/// </remarks>
public static class MemorialObservances
{
    /// <summary>The <paramref name="number"/>th day <paramref name="rule"/> is kept after a death on <paramref name="dateOfPassing"/>.</summary>
    public static DateOnly Date(DateOnly dateOfPassing, MemorialRule rule, int number)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        var years = rule.RepeatsAnnually ? number - 1 : 0;
        return dateOfPassing.AddMonths(rule.OffsetMonths + (12 * years)).AddDays(rule.OffsetDays);
    }

    /// <summary>Which time <paramref name="rule"/> is kept on <paramref name="date"/>, or <see langword="null"/> when it is not one of its days.</summary>
    public static int? NumberOn(DateOnly dateOfPassing, MemorialRule rule, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var first = Date(dateOfPassing, rule, 1);
        if (!rule.RepeatsAnnually || date <= first)
        {
            return date == first ? 1 : null;
        }

        // The year after the last one there is, never a day: it cannot be a later year's date.
        if (date.Year >= DateOnly.MaxValue.Year)
        {
            return null;
        }

        // A later year falls in its calendar year, give or take the days the rule adds.
        var guess = date.Year - first.Year + 1;
        for (var number = Math.Max(1, guess - 1); number <= guess + 1; number++)
        {
            if (Date(dateOfPassing, rule, number) == date)
            {
                return number;
            }
        }

        return null;
    }

    /// <summary>
    /// The observances a plan shows on <paramref name="today"/>, in date order: each rule kept once at
    /// its day, past or not, and a yearly rule at its next day on or after <paramref name="today"/>
    /// (its first, while that is still to come).
    /// </summary>
    public static IReadOnlyList<MemorialObservance> For(DateOnly dateOfPassing, IEnumerable<MemorialRule> rules, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules
            .Select(rule => rule.RepeatsAnnually ? Next(dateOfPassing, rule, today) : new MemorialObservance(rule, 1, Date(dateOfPassing, rule, 1)))
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Rule.SortOrder)
            .ToList();
    }

    /// <summary>A yearly rule's next day on or after <paramref name="today"/>.</summary>
    private static MemorialObservance Next(DateOnly dateOfPassing, MemorialRule rule, DateOnly today)
    {
        var first = Date(dateOfPassing, rule, 1);
        var number = Math.Max(1, today.Year - first.Year);
        while (Date(dateOfPassing, rule, number) < today)
        {
            number++;
        }

        return new MemorialObservance(rule, number, Date(dateOfPassing, rule, number));
    }
}
