using Sethika.Domain.Bookings;

namespace Sethika.Application.Abstractions;

/// <summary>A choice on the planner's form, a religion or a relationship: its code and its names.</summary>
public sealed record MemorialChoice(string Code, string Name, string? NameSi, string? NameTa);

/// <summary>A religion that has observances, with them, for the planner's form (plan 07, H6).</summary>
public sealed record MemorialReligionView(string Code, string Name, string? NameSi, string? NameTa, IReadOnlyList<MemorialRuleView> Rules);

/// <summary>An observance as the form explains it: its name and when it falls after the date of passing.</summary>
public sealed record MemorialRuleView(string Code, string Name, string? NameSi, string? NameTa, short OffsetMonths, short OffsetDays, bool RepeatsAnnually);

/// <summary>The ids behind the form's codes; <see langword="null"/> where a code names nothing active.</summary>
/// <param name="ReligionHasRules">Whether the religion has an active observance: a plan without one would show nothing.</param>
public sealed record MemorialLookups(int? Religion, bool ReligionHasRules, int? Relationship);

/// <summary>An active memorial plan as stored, with its codes, and its donor's language for a text.</summary>
/// <remarks>A class rather than a record: Dapper's constructor matching does not apply the DateOnly handler.</remarks>
public sealed class MemorialPlanRow
{
    public int Id { get; init; }
    public Guid PublicId { get; init; }
    public Guid DonorUserId { get; init; }
    public string DeceasedName { get; init; } = null!;
    public string? Relationship { get; init; }
    public DateOnly DateOfPassing { get; init; }
    public int ReligionLookupId { get; init; }
    public string Religion { get; init; } = null!;
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? Language { get; init; }
}

/// <summary>A live booking (requested or approved) made for a memorial plan, by the donor or by the yearly job.</summary>
public sealed class MemorialBookingRow
{
    public int MemorialPlanId { get; init; }
    public DateOnly Date { get; init; }
    public string BookingNo { get; init; } = null!;
    public string Status { get; init; } = null!;
    public string Institution { get; init; } = null!;
    public string InstitutionName { get; init; } = null!;
    public string? InstitutionNameSi { get; init; }
    public string? InstitutionNameTa { get; init; }
}

/// <summary>An active yearly booking made for a memorial plan: the day of the year it books.</summary>
public sealed class MemorialYearlyRow
{
    public int MemorialPlanId { get; init; }
    public byte Month { get; init; }
    public byte Day { get; init; }
    public Guid PublicId { get; init; }
}

/// <summary>Active plans and what the planner reads beside them: the active rules, the plans' live bookings and yearly bookings.</summary>
public sealed record MemorialPlansData(
    IReadOnlyList<MemorialPlanRow> Plans,
    IReadOnlyList<MemorialRule> Rules,
    IReadOnlyList<MemorialBookingRow> Bookings,
    IReadOnlyList<MemorialYearlyRow> Yearly);

/// <summary>Dapper reads for the Memorial Dana Planner (plan 07, H6, H8).</summary>
public interface IMemorialQueries
{
    /// <summary>The religions with an active observance, each with its observances, in the seed's order.</summary>
    Task<IReadOnlyList<MemorialReligionView>> ListReligionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The relationships a plan may name.</summary>
    Task<IReadOnlyList<MemorialChoice>> ListRelationshipsAsync(CancellationToken cancellationToken = default);

    /// <summary>The ids of <paramref name="religion"/> and <paramref name="relationship"/>, for a new plan.</summary>
    Task<MemorialLookups> ResolveAsync(string religion, string? relationship, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="donorId"/>'s active plans, or only <paramref name="plan"/> when given, with what
    /// the planner shows beside them. Under the donor's session.
    /// </summary>
    Task<MemorialPlansData> LoadAsync(Guid donorId, Guid? plan, CancellationToken cancellationToken = default);

    /// <summary>Every active plan, with the live bookings made for them from <paramref name="from"/> on: the reminder job's read, as the platform.</summary>
    Task<MemorialPlansData> LoadAllAsync(DateOnly from, CancellationToken cancellationToken = default);
}
