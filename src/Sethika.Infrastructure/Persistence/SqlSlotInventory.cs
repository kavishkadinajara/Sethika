using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="ISlotInventory"/> on the context's own connection and transaction.
/// </summary>
/// <remarks>
/// Three layers keep a slot-day from being overbooked (plan 03, C2). The Redis lock in front of this
/// class is only the first, and is optional. The two here are the guarantee:
/// <list type="number">
/// <item>The conditional UPDATE: it takes the place only where the row still has one, and its
/// exclusive row lock queues every other taker until this transaction ends. The next one then
/// re-reads the row as committed, so two takers never both see the last place.</item>
/// <item><c>CK_SlotInst_Sponsors</c> and <c>CK_SlotInst_Headcount</c>: whatever an UPDATE says, an
/// overbooked row cannot be stored. Their refusal (547) is read as "full" too, so even an UPDATE
/// that forgot its WHERE could not overbook.</item>
/// </list>
/// </remarks>
public sealed class SqlSlotInventory(SethikaDbContext context) : ISlotInventory
{
    private const string SlotDateKey = "UQ_SlotInst_SlotDate";
    private const int CheckConstraintViolation = 547;

    public async Task<SlotInstance> GetOrCreateAsync(MealSlot slot, DateOnly slotDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);
        RequireTransaction();

        var existing = await FindAsync(slot.Id, slotDate, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var created = SlotInstance.Open(slot, slotDate);
        context.SlotInstances.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Its counters change only in SQL from here, so a tracked copy would only go stale.
            context.Entry(created).State = EntityState.Detached;
            return created;
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == SlotDateKey)
        {
            // Another booking created it first and has committed (its key lock held this insert until
            // then). A caught 2627 does not doom the transaction while XACT_ABORT is off.
            context.Entry(created).State = EntityState.Detached;
            return await FindAsync(slot.Id, slotDate, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"{SlotDateKey} refused slot {slot.Id} on {slotDate:yyyy-MM-dd}, and no row is there.", ex);
        }
    }

    public async Task<bool> TryTakeAsync(long slotInstanceId, short headcount, CancellationToken cancellationToken = default)
    {
        RequireTransaction();

        try
        {
            var taken = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE booking.SlotInstances
                SET BookedSponsors  = BookedSponsors + 1,
                    BookedHeadcount = BookedHeadcount + {headcount},
                    Status = CASE WHEN BookedSponsors + 1 = MaxSponsors THEN 'Full' ELSE 'Open' END
                WHERE Id = {slotInstanceId}
                  AND Status = 'Open'
                  AND BookedSponsors < MaxSponsors
                  AND BookedHeadcount + {headcount} <= MaxHeadcount;
                """,
                cancellationToken).ConfigureAwait(false);

            return taken == 1;
        }
        catch (SqlException ex) when (ex.Number == CheckConstraintViolation)
        {
            return false;
        }
    }

    public async Task ReleaseAsync(long slotInstanceId, short headcount, CancellationToken cancellationToken = default)
    {
        RequireTransaction();

        // A Full day reopens; a Blocked or Closed one stays as it is.
        var released = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE booking.SlotInstances
            SET BookedSponsors  = BookedSponsors - 1,
                BookedHeadcount = BookedHeadcount - {headcount},
                Status = CASE WHEN Status = 'Full' THEN 'Open' ELSE Status END
            WHERE Id = {slotInstanceId};
            """,
            cancellationToken).ConfigureAwait(false);

        if (released != 1)
        {
            throw new InvalidOperationException($"Slot instance {slotInstanceId} was not there to give a place back to.");
        }
    }

    private Task<SlotInstance?> FindAsync(int mealSlotId, DateOnly date, CancellationToken cancellationToken) =>
        context.SlotInstances
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.MealSlotId == mealSlotId && i.SlotDate == date, cancellationToken);

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Slot counters change only inside the transaction that saves the booking, so a failed save gives the place back.");
        }
    }
}
