namespace Sethika.Application.Abstractions;

/// <summary>
/// The counts the sidebar shows beside its links (plan 05, R3). Each is read only for a caller
/// who may see the screen it counts; the caller decides that from the permission set, never here.
/// </summary>
public interface IShellQueries
{
    /// <summary>Booking requests of the institution still to approve or reject, from <paramref name="today"/> (Colombo) on.</summary>
    Task<int> CountBookingsToAnswerAsync(int institutionId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Registration requests still waiting for the platform (submitted, under review, or back with more information).</summary>
    Task<int> CountRegistrationsWaitingAsync(CancellationToken cancellationToken = default);
}
