using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary><see cref="IBookingRowLocks"/> on the context's own connection and transaction.</summary>
public sealed class SqlBookingRowLocks(SethikaDbContext context) : IBookingRowLocks
{
    public async Task LockAsync(long bookingId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A booking's row lock is held until the transaction that needs it ends; without one it " +
                "would be released before its work is saved.");
        }

        await context.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS Value FROM booking.Bookings WITH (UPDLOCK, ROWLOCK) WHERE Id = {bookingId};")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
