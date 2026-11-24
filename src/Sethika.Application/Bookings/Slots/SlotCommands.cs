using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Slots;

/// <summary>A meal slot as the institution's slot form sends it.</summary>
/// <param name="StartTime"><c>HH:mm</c>, as an HTML time input sends it, Colombo time.</param>
/// <param name="EndTime"><c>HH:mm</c>, after <paramref name="StartTime"/>.</param>
/// <param name="DaysOfWeekMask">One bit per day, Sunday 1 to Saturday 64 (§8, E9); 127 is every day.</param>
/// <param name="BookingMode"><c>Exclusive</c> or <c>Shared</c>.</param>
/// <param name="MaxSponsors">2-20 for a shared slot; an exclusive one is always 1.</param>
/// <param name="AllowedFulfilment">The <c>FulfilmentMode</c>s offered: <c>DonorBrings</c>, <c>InstitutionPrepares</c>.</param>
/// <param name="Version">An edit's: the slot's <c>RowVersion</c> as the editor read it. A new slot has none.</param>
public sealed record MealSlotInput(
    string? Name,
    string? NameSi,
    string? NameTa,
    string? StartTime,
    string? EndTime,
    byte? DaysOfWeekMask,
    string? BookingMode,
    byte? MaxSponsors,
    short? MinHeadcount,
    short? MaxHeadcount,
    decimal? EstimatedCostPerHead,
    short? MinAdvanceDays,
    short? MaxAdvanceDays,
    bool? RequiresApproval,
    IReadOnlyList<string>? AllowedFulfilment,
    short? SortOrder,
    byte[]? Version = null);

/// <summary>Days taken out of booking, for every slot or for one.</summary>
/// <param name="MealSlot">The one slot's <c>PublicId</c>, or <see langword="null"/> for all of them.</param>
public sealed record BlockedDateInput(DateOnly? DateFrom, DateOnly? DateTo, Guid? MealSlot, string? Reason);

/// <summary>The outcome of a change to the institution's slots, blocked dates or listing.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="PublicId">The slot created or changed.</param>
/// <param name="Version">The slot's <c>RowVersion</c> after the change, for the next edit.</param>
/// <param name="BlockedDateId">The blocked date added.</param>
/// <param name="AffectedBookings">
/// Active bookings the change falls on, which it does not cancel: the screen warns about them.
/// </param>
/// <param name="StandingDays">A block: the standing days it let go (plan 08, K7).</param>
public sealed record SlotChangeResult(
    string? Error,
    IReadOnlyList<string> Fields,
    Guid? PublicId,
    byte[]? Version,
    int? BlockedDateId,
    IReadOnlyList<string> AffectedBookings,
    int StandingDays = 0)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No slot with that id in the caller's institution.</summary>
    public const string SlotNotFound = "slot_not_found";

    /// <summary>No blocked date with that id in the caller's institution.</summary>
    public const string BlockedDateNotFound = "blocked_date_not_found";

    /// <summary>The institution already has a slot by that name (<c>UQ_MealSlots_Name</c>).</summary>
    public const string NameTaken = "slot_name_taken";

    /// <summary>Someone changed the slot since it was read; read it again.</summary>
    public const string Stale = "stale";

    /// <summary>An institution is listed only once it has a slot to book (C10).</summary>
    public const string NoActiveSlot = "no_active_slot";

    public bool Succeeded => Error is null;

    internal static SlotChangeResult Ok() => new(null, [], null, null, null, []);

    internal static SlotChangeResult ForSlot(MealSlot slot, IReadOnlyList<string>? affected = null) =>
        new(null, [], slot.PublicId, slot.RowVersion, null, affected ?? []);

    internal static SlotChangeResult ForBlock(int id, IReadOnlyList<string> affected, int standingDays) => new(null, [], null, null, id, affected, standingDays);

    internal static SlotChangeResult Failed(string error) => new(error, [], null, null, null, []);

    internal static SlotChangeResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null, null, []);
}
