using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Needs;

/// <summary>
/// Every move of a need's two counters (plan 09, M13, M15, M29, M30): one conditional UPDATE each, so a
/// need is its own inventory and the row itself serialises them, as a slot-day does (C2).
/// </summary>
/// <remarks>
/// All run inside the caller's transaction. <c>CK_Needs_Pledged</c> and <c>CK_Needs_Received</c> are the
/// backstop behind the conditions. Reading a need and writing it back would lose a move made in between,
/// so nothing writes these columns through a tracked <see cref="Need"/>.
/// </remarks>
internal static class NeedCounters
{
    /// <summary>Speaks for <paramref name="quantity"/> if the need is open and has room.</summary>
    /// <returns>1 when it did; 0 when not, and <see cref="WhyNotAsync"/> says why.</returns>
    public static Task<int> PledgeAsync(this ISethikaDbContext db, int needId, decimal quantity, CancellationToken cancellationToken) =>
        db.Needs
            .Where(n => n.Id == needId && n.Status == NeedStatus.Open && n.QuantityPledged + quantity <= n.QuantityNeeded)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.QuantityPledged, n => n.QuantityPledged + quantity), cancellationToken);

    /// <summary>A cancelled pledge gives its quantity back.</summary>
    public static Task<int> GiveBackAsync(this ISethikaDbContext db, int needId, decimal quantity, CancellationToken cancellationToken) =>
        db.Needs
            .Where(n => n.Id == needId)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.QuantityPledged, n => n.QuantityPledged - quantity), cancellationToken);

    /// <summary>A pledge delivered (M15): what came counts as received, what did not is given back.</summary>
    public static Task<int> DeliverAsync(this ISethikaDbContext db, int needId, decimal pledged, decimal received, DateTime now, CancellationToken cancellationToken) =>
        db.Needs
            .Where(n => n.Id == needId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(n => n.QuantityPledged, n => n.QuantityPledged - (pledged - received))
                    .SetProperty(n => n.QuantityReceived, n => n.QuantityReceived + received)
                    .SetProperty(n => n.Status, n => n.Status == NeedStatus.Open && n.QuantityReceived + received >= n.QuantityNeeded ? NeedStatus.Fulfilled : n.Status)
                    .SetProperty(n => n.EndedAt, n => n.Status == NeedStatus.Open && n.QuantityReceived + received >= n.QuantityNeeded ? now : n.EndedAt),
                cancellationToken);

    /// <summary>
    /// Items that came without a pledge (M15, M29): they take up room as a pledge would and arrive at
    /// once, so both counters move, if the need is open and has room.
    /// </summary>
    /// <returns>1 when it did; 0 when not, and <see cref="WhyNotAsync"/> says why.</returns>
    public static Task<int> CountArrivalAsync(this ISethikaDbContext db, int needId, decimal quantity, DateTime now, CancellationToken cancellationToken) =>
        db.Needs
            .Where(n => n.Id == needId && n.Status == NeedStatus.Open && n.QuantityPledged + quantity <= n.QuantityNeeded)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(n => n.QuantityPledged, n => n.QuantityPledged + quantity)
                    .SetProperty(n => n.QuantityReceived, n => n.QuantityReceived + quantity)
                    .SetProperty(n => n.Status, n => n.QuantityReceived + quantity >= n.QuantityNeeded ? NeedStatus.Fulfilled : n.Status)
                    .SetProperty(n => n.EndedAt, n => n.QuantityReceived + quantity >= n.QuantityNeeded ? now : n.EndedAt),
                cancellationToken);

    /// <summary>
    /// A voided gift leaves the need's totals (M30): the room it took is free again, and a need it had
    /// fulfilled is open again. A closed need stays closed, with the true count.
    /// </summary>
    public static Task<int> TakeBackArrivalAsync(this ISethikaDbContext db, int needId, decimal quantity, CancellationToken cancellationToken) =>
        db.Needs
            .Where(n => n.Id == needId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(n => n.QuantityPledged, n => n.QuantityPledged - quantity)
                    .SetProperty(n => n.QuantityReceived, n => n.QuantityReceived - quantity)
                    .SetProperty(n => n.Status, n => n.Status == NeedStatus.Fulfilled ? NeedStatus.Open : n.Status)
                    .SetProperty(n => n.EndedAt, n => n.Status == NeedStatus.Fulfilled ? null : n.EndedAt),
                cancellationToken);

    /// <summary>Why a conditional move found no row: an open need has no room, any other takes nothing.</summary>
    public static async Task<string> WhyNotAsync(this ISethikaDbContext db, int needId, CancellationToken cancellationToken)
    {
        var status = await db.Needs
            .AsNoTracking()
            .Where(n => n.Id == needId)
            .Select(n => n.Status)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return status == NeedStatus.Open ? NeedErrors.Full : NeedErrors.Closed;
    }
}
