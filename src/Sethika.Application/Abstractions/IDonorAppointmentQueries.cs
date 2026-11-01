namespace Sethika.Application.Abstractions;

/// <summary>One of the signed-in donor's service requests (Phase E, 8.5).</summary>
/// <param name="StartsAt">UTC.</param>
/// <param name="EndsAt">UTC.</param>
/// <param name="Institution">The institution's slug.</param>
/// <param name="ServiceType">The service type's <c>PublicId</c>.</param>
/// <param name="LocationMode"><c>AtInstitution</c> or <c>AtDonorPlace</c>.</param>
/// <param name="CanCancel">Whether the donor may still cancel it themselves (V13).</param>
/// <remarks>
/// No leaders: Row-Level Security keeps the roster and the assignments inside the institution (V9).
/// </remarks>
public sealed record DonorAppointmentView(
    string AppointmentNo,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    string Institution,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    Guid ServiceType,
    string ServiceName,
    string? ServiceNameSi,
    string? ServiceNameTa,
    string LocationMode,
    string? Address,
    string? Occasion,
    string? Note,
    DateTime CreatedAt,
    string? RejectReason,
    string? CancelReason,
    bool CanCancel);

/// <summary>Dapper reads for the donor's service requests.</summary>
public interface IDonorAppointmentQueries
{
    /// <summary>The donor's own requests, latest start first, at most <see cref="MaxMine"/>.</summary>
    /// <param name="utcNow">Now, for <see cref="DonorAppointmentView.CanCancel"/>.</param>
    Task<IReadOnlyList<DonorAppointmentView>> ListMineAsync(Guid donorId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>The most requests <see cref="ListMineAsync"/> returns; the list has no paging yet.</summary>
    const int MaxMine = 200;
}
