namespace Sethika.Application.Abstractions;

/// <summary>
/// Tells every calendar open on an institution that some of its days changed (plan 07, H9). A hint,
/// not the data: the calendar fetches its month again from the API, which stays the only source of
/// truth, so a lost or late hint costs freshness, never correctness.
/// </summary>
/// <remarks>The API host implements it with its SignalR hub; the Redis backplane carries it to every instance.</remarks>
public interface IAvailabilityNotifier
{
    /// <summary>The days <paramref name="firstDay"/> to <paramref name="lastDay"/> changed; both <see langword="null"/>: every day.</summary>
    Task ChangedAsync(string slug, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the outbox job now instead of at its next minute (plan 07, H10), so a change reaches open
/// calendars and texts within about a second. Fire and forget: if the wake is lost, the minute's run
/// delivers.
/// </summary>
public interface IOutboxWake
{
    void Wake();
}
