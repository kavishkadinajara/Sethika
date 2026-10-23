using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Slots;

/// <summary>
/// What an institution offers for booking (plan 03, 5.4): its meal slots, the days it blocks, and
/// whether it appears in the public list.
/// </summary>
/// <remarks>
/// Every method acts for <c>institutionId</c>, the institution in the caller's token. Meal slots and
/// blocked dates are public to read under Row-Level Security, so a lookup also checks the row is
/// that institution's: another institution's slot reads as not found, as one that does not exist.
///
/// Changing a slot never changes a slot-day already booked: its capacity was copied when its first
/// booking was made (C3). Blocking days or deactivating a slot does not cancel the bookings already
/// on them either; the result names them, so the screen can warn and the institution decides.
/// </remarks>
public sealed class SlotManagementHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    private const string NameIndex = "UQ_MealSlots_Name";
    private const int MaxName = 100;
    private const int MaxReason = 200;

    /// <summary>The longest block one request may add; a longer closure is several blocks.</summary>
    public const int MaxBlockDays = 366;

    /// <summary>The fulfilment a slot may offer: caterers are cut from plan 03 (§4).</summary>
    private static readonly string[] OfferableFulfilment = [FulfilmentMode.DonorBrings, FulfilmentMode.InstitutionPrepares];

    // ------------------------------------------------------------------ meal slots

    /// <summary>A new, active slot.</summary>
    public async Task<SlotChangeResult> CreateAsync(int institutionId, Guid userId, MealSlotInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var invalid = new List<string>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var slot = new MealSlot
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            CreatedAt = now,
            CreatedBy = userId,
        };

        Apply(input, slot, invalid);
        if (invalid.Count > 0)
        {
            return SlotChangeResult.Invalid(invalid);
        }

        db.MealSlots.Add(slot);
        return await SaveAsync(slot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the slot's details, if it is still the <see cref="MealSlotInput.Version"/> the editor read.</summary>
    public async Task<SlotChangeResult> UpdateAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        MealSlotInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var invalid = new List<string>();
        var slot = await FindSlotAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (slot is null)
        {
            return SlotChangeResult.Failed(SlotChangeResult.SlotNotFound);
        }

        if (input.Version is not { Length: 8 })
        {
            invalid.Add("version");
        }

        Apply(input, slot, invalid);
        if (invalid.Count > 0)
        {
            return SlotChangeResult.Invalid(invalid);
        }

        // The save's WHERE compares the version the editor read, not the one loaded just now: an
        // edit made in between, by anyone, makes this one stale instead of silently overwriting it.
        db.Entry(slot).Property(s => s.RowVersion).OriginalValue = input.Version!;
        slot.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        slot.UpdatedBy = userId;

        return await SaveAsync(slot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes a slot out of booking, or puts it back. Bookings already made on it stay; deactivating
    /// names those still to come.
    /// </summary>
    public async Task<SlotChangeResult> SetActiveAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var slot = await FindSlotAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (slot is null)
        {
            return SlotChangeResult.Failed(SlotChangeResult.SlotNotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (slot.IsActive != active)
        {
            slot.IsActive = active;
            slot.UpdatedAt = now;
            slot.UpdatedBy = userId;

            var saved = await SaveAsync(slot, cancellationToken).ConfigureAwait(false);
            if (!saved.Succeeded)
            {
                return saved;
            }
        }

        var affected = active
            ? []
            : await ActiveBookingsAsync(institutionId, slot.Id, SriLankaTime.Today(now), DateOnly.MaxValue, cancellationToken).ConfigureAwait(false);

        return SlotChangeResult.ForSlot(slot, affected);
    }

    // ------------------------------------------------------------------ blocked dates

    /// <summary>Takes days out of booking, for every slot or one. Bookings already on them stay, and are named.</summary>
    public async Task<SlotChangeResult> AddBlockedDateAsync(
        int institutionId,
        Guid userId,
        BlockedDateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invalid = new List<string>();
        var reason = Tidy(input.Reason);

        if (input.DateFrom is not { } from)
        {
            invalid.Add("dateFrom");
        }

        // A block that ended before today blocks nothing; one longer than a year is a mistake more
        // often than a plan.
        if (input.DateTo is not { } to
            || (input.DateFrom is { } start && (to < start || to.DayNumber - start.DayNumber >= MaxBlockDays))
            || to < SriLankaTime.Today(now))
        {
            invalid.Add("dateTo");
        }

        if (reason is null || reason.Length > MaxReason)
        {
            invalid.Add("reason");
        }

        if (invalid.Count > 0)
        {
            return SlotChangeResult.Invalid(invalid);
        }

        int? slotId = null;
        if (input.MealSlot is { } publicId)
        {
            slotId = await db.MealSlots
                .Where(s => s.PublicId == publicId && s.InstitutionId == institutionId)
                .Select(s => (int?)s.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (slotId is null)
            {
                return SlotChangeResult.Failed(SlotChangeResult.SlotNotFound);
            }
        }

        var block = new BlockedDate
        {
            InstitutionId = institutionId,
            MealSlotId = slotId,
            DateFrom = input.DateFrom!.Value,
            DateTo = input.DateTo!.Value,
            Reason = reason!,
            CreatedBy = userId,
            CreatedAt = now,
        };

        db.BlockedDates.Add(block);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var affected = await ActiveBookingsAsync(institutionId, slotId, block.DateFrom, block.DateTo, cancellationToken).ConfigureAwait(false);
        return SlotChangeResult.ForBlock(block.Id, affected);
    }

    /// <summary>Opens blocked days for booking again.</summary>
    public async Task<SlotChangeResult> RemoveBlockedDateAsync(int institutionId, int blockedDateId, CancellationToken cancellationToken = default)
    {
        var block = await db.BlockedDates
            .SingleOrDefaultAsync(b => b.Id == blockedDateId && b.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (block is null)
        {
            return SlotChangeResult.Failed(SlotChangeResult.BlockedDateNotFound);
        }

        db.BlockedDates.Remove(block);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SlotChangeResult.Ok();
    }

    // ------------------------------------------------------------------ the public list

    /// <summary>
    /// Shows the institution in the public list of institutions, or hides it (C10). It is shown only
    /// while it has an active slot to book; its <c>/i/{slug}</c> page is reachable either way.
    /// </summary>
    public async Task<SlotChangeResult> SetListedAsync(int institutionId, Guid userId, bool listed, CancellationToken cancellationToken = default)
    {
        if (listed && !await db.MealSlots.AnyAsync(s => s.InstitutionId == institutionId && s.IsActive, cancellationToken).ConfigureAwait(false))
        {
            return SlotChangeResult.Failed(SlotChangeResult.NoActiveSlot);
        }

        var institution = await db.Institutions
            .SingleAsync(i => i.Id == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (institution.IsPubliclyListed != listed)
        {
            // Not a permission, so no PermissionsVersion bump: only the list reads it.
            institution.IsPubliclyListed = listed;
            institution.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            institution.UpdatedBy = userId;

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return SlotChangeResult.Failed(SlotChangeResult.Stale);
            }
        }

        return SlotChangeResult.Ok();
    }

    // ------------------------------------------------------------------ plumbing

    private Task<MealSlot?> FindSlotAsync(int institutionId, Guid publicId, CancellationToken cancellationToken) =>
        db.MealSlots.SingleOrDefaultAsync(s => s.PublicId == publicId && s.InstitutionId == institutionId, cancellationToken);

    private async Task<SlotChangeResult> SaveAsync(MealSlot slot, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == NameIndex)
        {
            return SlotChangeResult.Failed(SlotChangeResult.NameTaken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SlotChangeResult.Failed(SlotChangeResult.Stale);
        }

        return SlotChangeResult.ForSlot(slot);
    }

    /// <summary>
    /// Requested and approved bookings of the institution on slot-days from <paramref name="first"/>
    /// to <paramref name="last"/>, of one slot or all.
    /// </summary>
    /// <remarks>
    /// The institution is named, not left to Row-Level Security, whose owner branch would add the
    /// caller's own bookings as a donor somewhere else (§8, E8).
    /// </remarks>
    private async Task<IReadOnlyList<string>> ActiveBookingsAsync(
        int institutionId,
        int? mealSlotId,
        DateOnly first,
        DateOnly last,
        CancellationToken cancellationToken) =>
        await (from b in db.Bookings
               join i in db.SlotInstances on b.SlotInstanceId equals i.Id
               where b.InstitutionId == institutionId
                     && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved)
                     && i.SlotDate >= first && i.SlotDate <= last
                     && (mealSlotId == null || i.MealSlotId == mealSlotId)
               orderby i.SlotDate, b.BookingNo
               select b.BookingNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Checks the form and copies it onto <paramref name="slot"/>, naming every field that is wrong.
    /// The table's CHECKs say the same, but a 547 cannot tell the screen which field to mark.
    /// </summary>
    private static void Apply(MealSlotInput input, MealSlot slot, List<string> invalid)
    {
        var name = Tidy(input.Name);
        if (name is null || name.Length > MaxName)
        {
            invalid.Add("name");
        }

        var nameSi = Tidy(input.NameSi);
        if (nameSi is { Length: > MaxName })
        {
            invalid.Add("nameSi");
        }

        var nameTa = Tidy(input.NameTa);
        if (nameTa is { Length: > MaxName })
        {
            invalid.Add("nameTa");
        }

        var start = Time(input.StartTime);
        if (start is null)
        {
            invalid.Add("startTime");
        }

        var end = Time(input.EndTime);
        if (end is null || (start is { } s && end <= s))
        {
            invalid.Add("endTime");
        }

        if (input.DaysOfWeekMask is not (>= 1 and <= MealSlot.EveryDay))
        {
            invalid.Add("daysOfWeekMask");
        }

        var mode = input.BookingMode?.Trim();
        byte sponsors = 1;
        if (mode == BookingMode.Exclusive)
        {
            if (input.MaxSponsors is not (null or 1))
            {
                invalid.Add("maxSponsors");
            }
        }
        else if (mode == BookingMode.Shared)
        {
            if (input.MaxSponsors is { } shared and >= 2 and <= 20)
            {
                sponsors = shared;
            }
            else
            {
                invalid.Add("maxSponsors");
            }
        }
        else
        {
            invalid.Add("bookingMode");
        }

        var minHeadcount = input.MinHeadcount ?? 1;
        if (minHeadcount < 1)
        {
            invalid.Add("minHeadcount");
        }

        if (input.MaxHeadcount is not { } maxHeadcount || maxHeadcount < Math.Max(minHeadcount, (short)1))
        {
            invalid.Add("maxHeadcount");
        }

        // DECIMAL(10,2): below a hundred million, in cents.
        if (input.EstimatedCostPerHead is { } cost && (cost < 0 || cost >= 100_000_000m || decimal.Round(cost, 2) != cost))
        {
            invalid.Add("estimatedCostPerHead");
        }

        var minAdvance = input.MinAdvanceDays ?? 1;
        if (minAdvance < 0)
        {
            invalid.Add("minAdvanceDays");
        }

        var maxAdvance = input.MaxAdvanceDays ?? 365;
        if (maxAdvance < Math.Max(minAdvance, (short)0))
        {
            invalid.Add("maxAdvanceDays");
        }

        var fulfilment = (input.AllowedFulfilment ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (fulfilment.Count == 0 || fulfilment.Exists(f => !OfferableFulfilment.Contains(f, StringComparer.Ordinal)))
        {
            invalid.Add("allowedFulfilment");
        }

        if (invalid.Count > 0)
        {
            return;
        }

        slot.Name = name!;
        slot.NameSi = nameSi;
        slot.NameTa = nameTa;
        slot.StartTime = start!.Value;
        slot.EndTime = end!.Value;
        slot.DaysOfWeekMask = input.DaysOfWeekMask!.Value;
        slot.BookingMode = mode!;
        slot.MaxSponsors = sponsors;
        slot.MinHeadcount = minHeadcount;
        slot.MaxHeadcount = input.MaxHeadcount!.Value;
        slot.EstimatedCostPerHead = input.EstimatedCostPerHead;
        slot.MinAdvanceDays = minAdvance;
        slot.MaxAdvanceDays = maxAdvance;
        slot.RequiresApproval = input.RequiresApproval ?? true;

        // In the order offered, so an unchanged choice saves as an unchanged column.
        slot.AllowedFulfilment = string.Join(',', OfferableFulfilment.Where(fulfilment.Contains));
        slot.SortOrder = input.SortOrder ?? 0;
    }

    /// <summary><c>HH:mm</c>, as an HTML time input sends it; the column keeps no seconds.</summary>
    private static TimeOnly? Time(string? value) =>
        TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
