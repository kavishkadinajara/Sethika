using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>
/// What <see cref="MealSlotRules"/> reads of a slot, as Dapper reads it: an institution's calendar
/// (<see cref="DonorBookingQueries"/>) and the search (<see cref="DiscoveryQueries"/>).
/// <c>TIME</c> arrives as a <see cref="TimeSpan"/>.
/// </summary>
internal sealed class SlotRuleRow
{
    public int Id { get; init; }
    public Guid PublicId { get; init; }
    public int InstitutionId { get; init; }
    public string Name { get; init; } = null!;
    public string? NameSi { get; init; }
    public string? NameTa { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public byte DaysOfWeekMask { get; init; }
    public string BookingMode { get; init; } = null!;
    public byte MaxSponsors { get; init; }
    public short MinHeadcount { get; init; }
    public short MaxHeadcount { get; init; }
    public short MinAdvanceDays { get; init; }
    public short MaxAdvanceDays { get; init; }
    public bool RequiresApproval { get; init; }
    public string AllowedFulfilment { get; init; } = null!;
    public bool IsActive { get; init; }

    public MealSlot ToMealSlot() => new()
    {
        Id = Id,
        PublicId = PublicId,
        InstitutionId = InstitutionId,
        Name = Name,
        NameSi = NameSi,
        NameTa = NameTa,
        StartTime = TimeOnly.FromTimeSpan(StartTime),
        EndTime = TimeOnly.FromTimeSpan(EndTime),
        DaysOfWeekMask = DaysOfWeekMask,
        BookingMode = BookingMode,
        MaxSponsors = MaxSponsors,
        MinHeadcount = MinHeadcount,
        MaxHeadcount = MaxHeadcount,
        MinAdvanceDays = MinAdvanceDays,
        MaxAdvanceDays = MaxAdvanceDays,
        RequiresApproval = RequiresApproval,
        AllowedFulfilment = AllowedFulfilment,
        IsActive = IsActive,
    };
}
