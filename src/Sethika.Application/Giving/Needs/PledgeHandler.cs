using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Donations;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Needs;

/// <summary>
/// Pledges against a need (plan 09, M13-M15): a donor's own pledge and cancel, and staff's cancel and
/// receiving.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PledgeAsync"/> and <see cref="CancelOwnAsync"/> run as the platform
/// (<see cref="IOwnPledges"/>, M14), because a donor's session may not write the need. So they take only
/// the caller's id and an address, and everything else is found from those: the pledge is written with
/// <c>DonorUserId</c> = the caller, and a pledge not the caller's reads as not found.
/// </para>
/// <para>
/// A need is one row, so its conditional UPDATE serialises all of this with no Redis lock (M13). It comes
/// first in the transaction: the row lock it takes also makes the count of a donor's open pledges safe.
/// </para>
/// </remarks>
public sealed class PledgeHandler(ISethikaDbContext db, INumberSeries numbers, TimeProvider timeProvider)
{
    /// <summary>The audit rows' entity.</summary>
    public const string Aggregate = "Pledge";

    /// <summary>Within <c>DECIMAL(10,2)</c>.</summary>
    public const decimal MaxQuantity = DonationHandler.MaxQuantity;

    /// <summary><c>org.Institutions.Status</c> of one that takes pledges.</summary>
    private const string Active = "Active";

    /// <summary>The caller pledges <see cref="PledgeInput.Quantity"/> of the need <paramref name="needId"/>.</summary>
    public async Task<NeedResult> PledgeAsync(Guid userId, Guid needId, PledgeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = SriLankaTime.Today(now);

        var invalid = new List<string>();
        if (input.Quantity is not { } quantity || quantity <= 0 || quantity > MaxQuantity || decimal.Round(quantity, 2) != quantity)
        {
            invalid.Add("quantity");
        }

        if (input.ExpectedDate is { } expected && (expected < today || expected > today.AddYears(1)))
        {
            invalid.Add("expectedDate");
        }

        if (invalid.Count > 0)
        {
            return NeedResult.Invalid([.. invalid]);
        }

        var hasPhone = await db.Users
            .AnyAsync(u => u.Id == userId && u.PhoneConfirmed && u.PhoneNumber != null, cancellationToken)
            .ConfigureAwait(false);

        if (!hasPhone)
        {
            return NeedResult.Failed(NeedResult.PhoneRequired);
        }

        // As the platform this reads every need, so what is public is decided here: a draft is nobody's
        // business, and an institution no longer active takes no pledges.
        var need = await db.Needs
            .AsNoTracking()
            .Where(n => n.PublicId == needId && n.Status != NeedStatus.Draft)
            .Join(
                db.Institutions.Where(i => i.Status == Active && !i.IsDeleted),
                n => n.InstitutionId,
                i => i.Id,
                (n, i) => new { n.Id, n.InstitutionId, n.Deadline })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (need is null)
        {
            return NeedResult.Failed(NeedErrors.NotFound);
        }

        if (need.Deadline < today)
        {
            return NeedResult.Failed(NeedErrors.Closed);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (await db.PledgeAsync(need.Id, input.Quantity!.Value, cancellationToken).ConfigureAwait(false) != 1)
        {
            return NeedResult.Failed(await db.WhyNotAsync(need.Id, cancellationToken).ConfigureAwait(false));
        }

        var open = await db.Pledges
            .CountAsync(p => p.NeedId == need.Id && p.DonorUserId == userId && p.Status == PledgeStatus.Pledged, cancellationToken)
            .ConfigureAwait(false);

        if (open >= Pledge.MaxOpenPerDonor)
        {
            return NeedResult.Failed(NeedErrors.TooManyPledges);
        }

        var pledge = new Pledge
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = need.InstitutionId,
            NeedId = need.Id,
            DonorUserId = userId,
            Quantity = input.Quantity.Value,
            ExpectedDeliveryDate = input.ExpectedDate,
            CreatedAt = now,
        };

        db.Pledges.Add(pledge);
        Audit(pledge, userId, "Pledge", now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return NeedResult.For(pledge.PublicId);
    }

    /// <summary>The caller cancels their own pledge, if it is not delivered yet.</summary>
    public async Task<NeedResult> CancelOwnAsync(Guid userId, Guid pledgeId, CancellationToken cancellationToken = default)
    {
        var pledge = await db.Pledges
            .SingleOrDefaultAsync(p => p.PublicId == pledgeId && p.DonorUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (pledge is null)
        {
            return NeedResult.Failed(NeedErrors.PledgeNotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (pledge.Cancel(userId, reason: null, reasonRequired: false, now) is { } refused)
        {
            return NeedResult.Failed(refused);
        }

        // A delivery racing this one changed the row first: then it is no longer open.
        return await SaveCancelAsync(pledge, userId, now, NeedErrors.PledgeNotOpen, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Staff cancel a pledge, with a reason the donor will read (M13).</summary>
    public async Task<NeedResult> CancelAsync(int institutionId, Guid userId, Guid pledgeId, CancelPledgeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var pledge = await FindAsync(institutionId, pledgeId, cancellationToken).ConfigureAwait(false);
        if (pledge is null)
        {
            return NeedResult.Failed(NeedErrors.PledgeNotFound);
        }

        if (input.Version is not { Length: 8 })
        {
            return NeedResult.Invalid("version");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refused = pledge.Cancel(userId, input.Reason, reasonRequired: true, now);
        if (refused == NeedErrors.InvalidReason)
        {
            return NeedResult.Invalid("reason");
        }

        if (refused is not null)
        {
            return NeedResult.Failed(refused);
        }

        db.Entry(pledge).Property(p => p.RowVersion).OriginalValue = input.Version;
        return await SaveCancelAsync(pledge, userId, now, NeedResult.Stale, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Staff mark a pledge delivered (M15): one transaction records the in-kind gift with its receipt
    /// number and text, and moves the need's counters, which may fulfil it.
    /// </summary>
    public async Task<NeedResult> ReceiveAsync(int institutionId, Guid userId, Guid pledgeId, ReceiveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var pledge = await FindAsync(institutionId, pledgeId, cancellationToken).ConfigureAwait(false);
        if (pledge is null)
        {
            return NeedResult.Failed(NeedErrors.PledgeNotFound);
        }

        if (input.Version is not { Length: 8 })
        {
            return NeedResult.Invalid("version");
        }

        if (input.Quantity is not { } quantity)
        {
            return NeedResult.Invalid("quantity");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refused = pledge.Receive(userId, quantity, now);
        if (refused == NeedErrors.InvalidQuantity)
        {
            return NeedResult.Invalid("quantity");
        }

        if (refused is not null)
        {
            return NeedResult.Failed(refused);
        }

        var title = await db.Needs
            .Where(n => n.Id == pledge.NeedId)
            .Select(n => n.Title)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var donor = await db.Users
            .Where(u => u.Id == pledge.DonorUserId)
            .Select(u => new { u.FullName, u.PhoneNumber })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var scope = await db.Institutions
            .Where(i => i.Id == institutionId)
            .Select(i => i.InstitutionCode)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var receiptNo = await numbers
            .NextAsync(NumberSeriesDefinition.Receipt, scope, SriLankaTime.Today(now).Year, cancellationToken)
            .ConfigureAwait(false);

        await db.DeliverAsync(pledge.NeedId, pledge.Quantity, quantity, now, cancellationToken).ConfigureAwait(false);

        var gift = new Donation
        {
            PublicId = Guid.CreateVersion7(now),
            ReceiptNo = receiptNo,
            InstitutionId = institutionId,
            DonorUserId = pledge.DonorUserId,
            DonorName = donor.FullName,
            DonorPhone = donor.PhoneNumber,
            Kind = Donation.InKind,
            ItemDescription = title,
            Quantity = quantity,
            NeedId = pledge.NeedId,
            PledgeId = pledge.Id,
            DonatedAt = now,
            RecordedBy = userId,
            RecordedAt = now,
        };

        db.Donations.Add(gift);
        db.OutboxMessages.Add(DonationTexts.Message(gift.PublicId, now));
        db.Entry(pledge).Property(p => p.RowVersion).OriginalValue = input.Version;
        Audit(pledge, userId, "Receive", now);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Rolled back as the transaction ends: the counters and the receipt number with it.
            return NeedResult.Failed(NeedResult.Stale);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return NeedResult.Receipt(pledge.PublicId, receiptNo, gift.PublicId);
    }

    private Task<Pledge?> FindAsync(int institutionId, Guid pledgeId, CancellationToken cancellationToken) =>
        db.Pledges.SingleOrDefaultAsync(p => p.PublicId == pledgeId && p.InstitutionId == institutionId, cancellationToken);

    /// <summary>The cancelled pledge and the quantity it gives back, in one transaction.</summary>
    private async Task<NeedResult> SaveCancelAsync(Pledge pledge, Guid userId, DateTime now, string whenChanged, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await db.GiveBackAsync(pledge.NeedId, pledge.Quantity, cancellationToken).ConfigureAwait(false);
        Audit(pledge, userId, "Cancel", now);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NeedResult.Failed(whenChanged);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return NeedResult.For(pledge.PublicId);
    }

    private void Audit(Pledge pledge, Guid userId, string action, DateTime now) =>
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = userId,
            InstitutionId = pledge.InstitutionId,
            Action = action,
            EntityName = Aggregate,
            EntityId = pledge.PublicId.ToString(),
            // Figures only: no names or phones.
            NewValues = JsonSerializer.Serialize(new { status = pledge.Status, quantity = pledge.Quantity, received = pledge.ReceivedQuantity }),
            OccurredAt = now,
        });
}
