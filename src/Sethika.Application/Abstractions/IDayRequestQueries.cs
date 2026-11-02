namespace Sethika.Application.Abstractions;

/// <summary>A day request in the institution's tray (plan 08, K6).</summary>
/// <param name="Holders">Open: the regular donors holding the day now, each its sponsorship and the donor's name.</param>
/// <param name="BookingNo">Given: the asker's booking.</param>
public sealed record InstitutionDayRequestView(
    string RequestNo,
    string Status,
    DateOnly Date,
    Guid MealSlot,
    string MealSlotName,
    string DonorName,
    string? DonorPhone,
    short Headcount,
    string Fulfilment,
    string? Note,
    DateTime CreatedAt,
    string? BookingNo,
    string? DeclineReason,
    IReadOnlyList<DayHolder> Holders);

/// <summary>A regular donor holding a requested day.</summary>
public sealed record DayHolder(Guid Sponsorship, string DonorName);

/// <summary>A donor's own day request.</summary>
/// <param name="Institution">The institution's slug.</param>
public sealed record MyDayRequestView(
    string RequestNo,
    string Status,
    DateOnly Date,
    string Institution,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    Guid MealSlot,
    string MealSlotName,
    short Headcount,
    string Fulfilment,
    DateTime CreatedAt,
    string? BookingNo,
    string? DeclineReason);

/// <summary>Reads of day requests, in the caller's own session (RLS <c>fn_TenantOrOwner</c>).</summary>
public interface IDayRequestQueries
{
    /// <summary>The most a list returns.</summary>
    const int MaxList = 200;

    /// <summary>The institution's requests, open ones only unless <paramref name="all"/>: the oldest open first, then the newest answered.</summary>
    Task<IReadOnlyList<InstitutionDayRequestView>> ListForInstitutionAsync(int institutionId, bool all, CancellationToken cancellationToken = default);

    /// <summary>The donor's own requests, newest first.</summary>
    Task<IReadOnlyList<MyDayRequestView>> ListMineAsync(Guid donorId, CancellationToken cancellationToken = default);
}
