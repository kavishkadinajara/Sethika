using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Bookings.Recurring;

/// <summary>The donor's changes to their yearly bookings (plan 07, H5).</summary>
public sealed class RecurringBookingHandler(ISethikaDbContext db)
{
    /// <summary>No active yearly booking of this donor's with that id.</summary>
    public const string NotFound = "recurring_not_found";

    /// <summary>
    /// Stops <paramref name="publicId"/>: no year after this one is booked. The row stays, and a year
    /// already booked stays booked; the donor cancels it the usual way. <see langword="null"/> on
    /// success, else <see cref="NotFound"/>.
    /// </summary>
    /// <remarks>
    /// Row-Level Security shows a donor only their own rows; the donor id is checked as well, so an
    /// institution's session could not stop one either.
    /// </remarks>
    public async Task<string?> StopAsync(Guid publicId, Guid donorId, CancellationToken cancellationToken = default)
    {
        var stopped = await db.RecurringBookings
            .Where(r => r.PublicId == publicId && r.DonorUserId == donorId && r.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.IsActive, false), cancellationToken)
            .ConfigureAwait(false);

        return stopped == 1 ? null : NotFound;
    }
}
