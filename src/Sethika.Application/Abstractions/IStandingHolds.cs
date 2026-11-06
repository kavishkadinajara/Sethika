namespace Sethika.Application.Abstractions;

/// <summary>A slot-day's active standing bookings, counted: who holds it is not said.</summary>
/// <param name="Headcount">The most people one of them brings.</param>
public sealed record StandingHold(int MealSlotId, DateOnly Date, int Sponsors, short Headcount);

/// <summary>
/// Which of an institution's slot-days its regular donors hold (plan 08, K6, K25), for the public
/// calendar's "held" and for asking: read past Row-Level Security, so only counts.
/// </summary>
public interface IStandingHolds
{
    /// <summary>The slot-days from <paramref name="first"/> to <paramref name="last"/> with at least one active standing booking.</summary>
    Task<IReadOnlyList<StandingHold>> ListAsync(int institutionId, DateOnly first, DateOnly last, CancellationToken cancellationToken = default);
}
