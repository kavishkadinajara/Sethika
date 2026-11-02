using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>
/// The institution sets up a standing sponsorship for one of its donors, ends it, and lets a day of it
/// go when the sponsor cannot give (plan 08, K2-K5, K9).
/// </summary>
/// <remarks>
/// <para>
/// Every method acts for <c>institutionId</c>, the institution in the caller's token; a sponsorship,
/// donor or slot of another institution reads as not found, by the query and by Row-Level Security.
/// </para>
/// <para>
/// Setting one up saves it, then books the days of its slot's booking window in the next
/// <see cref="BookedWhileWaitingDays"/> through the engine (<see cref="StandingDayBooker"/>), one
/// transaction a day, and answers with what became of each (K9). The window's later days go to the
/// standing job, started at once. A day the engine refuses is tried again by the nightly run. If the
/// request stops halfway, the sponsorship is saved and the job books the rest.
/// </para>
/// <para>
/// Ending it and skipping a day take the sponsorship's row lock first (<see cref="IStandingLocks"/>), as
/// the engine does before booking one of its days, so neither can cross a day being booked.
/// </para>
/// </remarks>
public sealed class StandingSponsorshipHandler(
    ISethikaDbContext db,
    StandingDayBooker booker,
    ISlotLocks locks,
    IStandingLocks standingLocks,
    ISlotInventory inventory,
    IStandingBookingWake wake,
    TimeProvider timeProvider)
{
    /// <summary>
    /// How far ahead setting one up books while staff wait: a month, whose clashes they see at once. A
    /// day takes about 50 ms, so a daily sponsorship's whole year would keep them for most of a minute;
    /// the rest of the window is the standing job's, started at once (<see cref="IStandingBookingWake"/>).
    /// </summary>
    public const int BookedWhileWaitingDays = 31;

    /// <summary>A booking's cancel reason when its sponsorship ends and none is given.</summary>
    public const string EndedReason = "The standing sponsorship ended.";

    /// <summary>A booking's cancel reason when its day is skipped and none is given.</summary>
    public const string SkippedReason = "The standing sponsor cannot give on this day.";

    private const string ExceptionDayIndex = "UQ_StandingExc_Day";
    private const int MaxInMemoryOf = 200;
    private const int MaxNotes = 500;

    /// <summary>Saves a new sponsorship and books the next days its slot's booking window holds now.</summary>
    public async Task<SponsorshipChangeResult> CreateAsync(int institutionId, Guid userId, SponsorshipInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = SriLankaTime.Today(now);

        var donorId = input.Donor is { } donor
            ? await db.InstitutionDonors
                .Where(d => d.PublicId == donor && d.InstitutionId == institutionId && d.IsActive)
                .Select(d => (int?)d.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var slot = input.MealSlot is { } slotId
            ? await db.MealSlots
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.PublicId == slotId && s.InstitutionId == institutionId && s.IsActive, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var sponsorship = new StandingSponsorship
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            CreatedBy = userId,
            CreatedAt = now,
        };

        var invalid = new List<string>();
        if (donorId is null)
        {
            invalid.Add("donor");
        }

        if (slot is null)
        {
            invalid.Add("mealSlot");
        }

        ApplyPattern(input, sponsorship, today, invalid);
        ApplyMeal(input, sponsorship, slot, invalid);

        if (invalid.Count > 0)
        {
            return SponsorshipChangeResult.Invalid(invalid);
        }

        sponsorship.InstitutionDonorId = donorId!.Value;
        sponsorship.MealSlotId = slot!.Id;

        db.StandingSponsorships.Add(sponsorship);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var publicId = sponsorship.PublicId;
        var version = sponsorship.RowVersion;

        // Each day after the save, in its own transaction: the booker clears the tracker between days.
        var window = sponsorship.DaysBetween(today, today.AddDays(slot.MaxAdvanceDays)).ToList();
        var waiting = window.Where(d => d.DayNumber - today.DayNumber <= BookedWhileWaitingDays).ToList();
        var days = new List<StandingDayResult>();
        foreach (var day in waiting)
        {
            days.Add(await booker.BookDayAsync(sponsorship.Id, day, cancellationToken).ConfigureAwait(false));
        }

        var later = window.Count - waiting.Count;
        if (later > 0)
        {
            wake.Wake();
        }

        return SponsorshipChangeResult.For(publicId, version, days, later);
    }

    /// <summary>
    /// Ends the sponsorship, if it is still the <paramref name="version"/> the screen read, and lets go
    /// every booked day after today. Today's meal stays: it is being made.
    /// </summary>
    public async Task<SponsorshipChangeResult> EndAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        byte[]? version,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<string>();
        if (version is not { Length: 8 })
        {
            fields.Add("version");
        }

        if (reason?.Trim().Length > Booking.MaxReason)
        {
            fields.Add("reason");
        }

        if (fields.Count > 0)
        {
            return SponsorshipChangeResult.Invalid(fields);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var sponsorship = await db.StandingSponsorships
            .SingleOrDefaultAsync(s => s.PublicId == publicId && s.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (sponsorship is null)
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.NotFound);
        }

        if (!await standingLocks.LockActiveAsync(sponsorship.Id, cancellationToken).ConfigureAwait(false))
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.Ended);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = SriLankaTime.Today(now);

        db.Entry(sponsorship).Property(s => s.RowVersion).OriginalValue = version!;
        sponsorship.End(userId, now);

        var days = await db.Bookings
            .Where(b => b.StandingSponsorshipId == sponsorship.Id
                        && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved))
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { Booking = b, i.SlotDate })
            .Where(d => d.SlotDate > today)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var cancelReason = Tidy(reason) ?? EndedReason;
        foreach (var day in days)
        {
            day.Booking.CancelAsInstitution(cancelReason, userId, now);
        }

        if (days.Count > 0)
        {
            db.OutboxMessages.Add(AvailabilityChange.Message(institutionId, days.Min(d => d.SlotDate), days.Max(d => d.SlotDate), now));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.Stale);
        }

        // After the save, as a decision gives a place back: the save is what says nobody else moved first.
        foreach (var day in days)
        {
            await inventory.ReleaseAsync(day.Booking.SlotInstanceId, day.Booking.Headcount, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return SponsorshipChangeResult.For(sponsorship.PublicId, sponsorship.RowVersion, []);
    }

    /// <summary>
    /// "The sponsor cannot give that day" (K5): the day is let go for good - its booking, if made, is
    /// cancelled and the place opens for others - and recorded, so the job never books it again (K4).
    /// </summary>
    public async Task<SponsorshipChangeResult> SkipAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        DateOnly? date,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var sponsorship = await db.StandingSponsorships
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.PublicId == publicId && s.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (sponsorship is null)
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.NotFound);
        }

        if (!sponsorship.IsActive)
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.Ended);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fields = new List<string>();

        // A day of its pattern, today or later: a past day has been given or not, and nothing changes that.
        if (date is not { } day || day < SriLankaTime.Today(now) || !sponsorship.OccursOn(day))
        {
            fields.Add("date");
        }

        if (reason?.Trim().Length > StandingDayChange.MaxReason)
        {
            fields.Add("reason");
        }

        if (fields.Count > 0)
        {
            return SponsorshipChangeResult.Invalid(fields);
        }

        await using var slotLock = await locks.AcquireAsync(sponsorship.MealSlotId, date!.Value, cancellationToken).ConfigureAwait(false);
        if (slotLock.Outcome == LockOutcome.Busy)
        {
            return SponsorshipChangeResult.Failed(BookMealResult.SlotBusy);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (!await standingLocks.LockActiveAsync(sponsorship.Id, cancellationToken).ConfigureAwait(false))
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.Ended);
        }

        // The day's booking in whatever state: one the institution already cancelled has no place to
        // give back, and one already given (completed, no-show) cannot be skipped.
        var booking = await db.Bookings
            .Where(b => b.StandingSponsorshipId == sponsorship.Id)
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { Booking = b, i.SlotDate })
            .Where(d => d.SlotDate == date.Value)
            .OrderByDescending(d => d.Booking.Id)
            .Select(d => d.Booking)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var tidy = Tidy(reason);
        var released = booking is not null && BookingStatus.HoldsPlace(booking.Status) ? booking : null;
        if (released is not null)
        {
            var cancelled = released.CancelAsInstitution(tidy ?? SkippedReason, userId, now);
            if (!cancelled.Succeeded)
            {
                return SponsorshipChangeResult.Failed(cancelled.Error!);
            }

            db.OutboxMessages.Add(AvailabilityChange.Message(institutionId, date.Value, date.Value, now));
        }

        db.StandingExceptions.Add(new StandingDayChange
        {
            InstitutionId = institutionId,
            StandingSponsorshipId = sponsorship.Id,
            ExceptionDate = date.Value,
            Kind = StandingDayChange.Skipped,
            BookingId = released?.Id,
            Reason = tidy,
            CreatedBy = userId,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == ExceptionDayIndex)
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.DayAlreadyChanged);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SponsorshipChangeResult.Failed(SponsorshipChangeResult.Stale);
        }

        if (released is not null)
        {
            await inventory.ReleaseAsync(released.SlotInstanceId, released.Headcount, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return SponsorshipChangeResult.Done();
    }

    /// <summary>
    /// The pattern and its dates, as <c>CK_Standing_Shape</c> wants them: each pattern's own fields and
    /// no others, so a field sent for another pattern is refused rather than quietly dropped.
    /// </summary>
    private static void ApplyPattern(SponsorshipInput input, StandingSponsorship sponsorship, DateOnly today, List<string> invalid)
    {
        var pattern = input.Pattern?.Trim();
        if (pattern is null || !StandingPattern.All.Contains(pattern, StringComparer.Ordinal))
        {
            invalid.Add("pattern");
            return;
        }

        var before = invalid.Count;
        sponsorship.Pattern = pattern;
        var weekly = pattern == StandingPattern.Weekly;
        var monthly = pattern == StandingPattern.Monthly;
        var yearly = pattern == StandingPattern.Yearly;

        sponsorship.MonthInterval = 1;
        if (input.MonthInterval is { } interval)
        {
            if (!monthly || interval < 1 || interval > StandingSponsorship.MaxMonthInterval)
            {
                invalid.Add("monthInterval");
            }
            else
            {
                sponsorship.MonthInterval = (byte)interval;
            }
        }

        sponsorship.WeekDays = StandingPattern.WeekMask(input.WeekDays);
        if (weekly ? sponsorship.WeekDays is null : input.WeekDays is not null)
        {
            invalid.Add("weekDays");
        }

        sponsorship.MonthDays = StandingPattern.MonthMask(input.MonthDays);
        if (monthly ? sponsorship.MonthDays is null : input.MonthDays is not null)
        {
            invalid.Add("monthDays");
        }

        if (!yearly)
        {
            if (input.YearMonth is not null)
            {
                invalid.Add("yearMonth");
            }

            if (input.YearDay is not null)
            {
                invalid.Add("yearDay");
            }
        }
        else if (input.YearMonth is not { } month || month < 1 || month > 12)
        {
            invalid.Add("yearMonth");
        }
        else if (input.YearDay is not { } yearDay || !StandingPattern.IsDayOfSomeYear(month, yearDay))
        {
            invalid.Add("yearDay");
        }
        else
        {
            sponsorship.YearMonth = (byte)month;
            sponsorship.YearDay = (byte)yearDay;
        }

        if (input.StartDate is not { } start || start < today)
        {
            invalid.Add("startDate");
            return;
        }

        sponsorship.StartDate = start;
        if (pattern == StandingPattern.Once)
        {
            // One day: its end is its start, sent or not.
            if (input.EndDate is { } end && end != start)
            {
                invalid.Add("endDate");
            }

            sponsorship.EndDate = start;
        }
        else if (input.EndDate is { } end && end < start)
        {
            invalid.Add("endDate");
        }
        else
        {
            sponsorship.EndDate = input.EndDate;
        }

        // A pattern that never meets its dates (the 31st every 12 months from April, 29 February
        // until next year) would be a sponsorship of no days.
        if (invalid.Count == before && sponsorship.NextDays(start, 1).Count == 0)
        {
            invalid.Add("pattern");
        }
    }

    /// <summary>The meal: a headcount and a way of providing it the slot takes, and the free text.</summary>
    private static void ApplyMeal(SponsorshipInput input, StandingSponsorship sponsorship, MealSlot? slot, List<string> invalid)
    {
        if (input.Headcount is not { } headcount || headcount <= 0
            || (slot is not null && (headcount < slot.MinHeadcount || headcount > slot.MaxHeadcount)))
        {
            invalid.Add("headcount");
        }
        else
        {
            sponsorship.Headcount = headcount;
        }

        var fulfilment = Tidy(input.Fulfilment);
        if (fulfilment is null || (slot is not null && !slot.Offers(fulfilment)))
        {
            invalid.Add("fulfilment");
        }
        else
        {
            sponsorship.FulfilmentMode = fulfilment;
        }

        sponsorship.InMemoryOf = Tidy(input.InMemoryOf);
        if (sponsorship.InMemoryOf is { Length: > MaxInMemoryOf })
        {
            invalid.Add("inMemoryOf");
        }

        sponsorship.Notes = Tidy(input.Notes);
        if (sponsorship.Notes is { Length: > MaxNotes })
        {
            invalid.Add("notes");
        }
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
