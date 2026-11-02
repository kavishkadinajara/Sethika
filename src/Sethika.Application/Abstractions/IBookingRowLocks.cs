namespace Sethika.Application.Abstractions;

/// <summary>Row locks on a booking, held by the context's open transaction until it ends.</summary>
public interface IBookingRowLocks
{
    /// <summary>
    /// Takes an update lock on booking <paramref name="bookingId"/>'s row, so changes counted against
    /// one booking - its invitations (plan 07, H16) - are made one at a time: two at once cannot both
    /// see room under the limit. The row itself is not changed, so its <c>RowVersion</c> stays.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task LockAsync(long bookingId, CancellationToken cancellationToken = default);
}
