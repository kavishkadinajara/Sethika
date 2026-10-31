using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the donor's side of booking (plan 03, 5.5).</summary>
/// <remarks>
/// The institution reads run anonymously: <c>org.Institutions</c>, <c>MealSlots</c>, their seasons,
/// <c>BlockedDates</c> and <c>SlotInstances</c> are public under Row-Level Security (C8), so what they
/// show is decided here, by the <c>Active</c> and listed filters and the columns chosen, not by RLS.
/// The donor's own bookings name the donor, as well as RLS's owner branch: a staff member signed in
/// for an institution would otherwise see its bookings too.
/// </remarks>
public sealed class DonorBookingQueries(IDbConnectionFactory connections) : IDonorBookingQueries
{
    private const string InstitutionColumns =
        """
        i.Slug AS Slug, i.Name AS Name, i.NameSi AS NameSi, i.NameTa AS NameTa,
        t.Code AS Type, t.Name AS TypeName, t.NameSi AS TypeNameSi, t.NameTa AS TypeNameTa,
        d.Name AS District, d.NameSi AS DistrictSi, d.NameTa AS DistrictTa, i.City AS City
        """;

    private const string ListSql =
        $"""
        SELECT {InstitutionColumns}
        FROM org.Institutions AS i
        JOIN org.InstitutionTypes AS t ON t.Id = i.InstitutionTypeId
        JOIN core.Districts AS d ON d.Id = i.DistrictId
        WHERE i.Status = 'Active' AND i.IsPubliclyListed = 1 AND i.IsDeleted = 0
        ORDER BY i.Name, i.Slug;
        """;

    // An active institution is reachable by its slug whether or not it is listed (C10): the link is
    // what an institution shares.
    private const string FindSql =
        """
        DECLARE @institutionId INT =
            (SELECT i.Id FROM org.Institutions AS i WHERE i.Slug = @slug AND i.Status = 'Active' AND i.IsDeleted = 0);
        """;

    private const string ProfileSql =
        $"""
        {FindSql}

        SELECT {InstitutionColumns},
               i.AddressLine1 AS AddressLine1, i.AddressLine2 AS AddressLine2, i.Description AS Description,
               i.DietaryNotes AS DietaryNotes, i.Latitude AS Latitude, i.Longitude AS Longitude
        FROM org.Institutions AS i
        JOIN org.InstitutionTypes AS t ON t.Id = i.InstitutionTypeId
        JOIN core.Districts AS d ON d.Id = i.DistrictId
        WHERE i.Id = @institutionId;

        SELECT s.PublicId AS PublicId, s.Name AS Name, s.NameSi AS NameSi, s.NameTa AS NameTa,
               CONVERT(CHAR(5), s.StartTime, 108) AS StartTime, CONVERT(CHAR(5), s.EndTime, 108) AS EndTime,
               s.DaysOfWeekMask AS DaysOfWeekMask, s.BookingMode AS BookingMode, s.MaxSponsors AS MaxSponsors,
               s.MinHeadcount AS MinHeadcount, s.MaxHeadcount AS MaxHeadcount,
               s.EstimatedCostPerHead AS EstimatedCostPerHead, s.MinAdvanceDays AS MinAdvanceDays,
               s.MaxAdvanceDays AS MaxAdvanceDays, s.RequiresApproval AS RequiresApproval,
               s.AllowedFulfilment AS AllowedFulfilment
        FROM booking.MealSlots AS s
        WHERE s.InstitutionId = @institutionId AND s.IsActive = 1
        ORDER BY s.SortOrder, s.StartTime, s.Name;
        """;

    // Every season of the slots, not only the month's: a slot with a season elsewhere in the year is
    // out of season in this month (C4).
    private const string AvailabilitySql =
        $"""
        {FindSql}

        SELECT i.Status AS Status FROM org.Institutions AS i WHERE i.Id = @institutionId;

        SELECT s.Id AS Id, s.PublicId AS PublicId, s.InstitutionId AS InstitutionId, s.Name AS Name,
               s.StartTime AS StartTime, s.EndTime AS EndTime, s.DaysOfWeekMask AS DaysOfWeekMask,
               s.BookingMode AS BookingMode, s.MaxSponsors AS MaxSponsors, s.MinHeadcount AS MinHeadcount,
               s.MaxHeadcount AS MaxHeadcount, s.MinAdvanceDays AS MinAdvanceDays,
               s.MaxAdvanceDays AS MaxAdvanceDays, s.RequiresApproval AS RequiresApproval,
               s.AllowedFulfilment AS AllowedFulfilment, s.IsActive AS IsActive
        FROM booking.MealSlots AS s
        WHERE s.InstitutionId = @institutionId AND s.IsActive = 1
        ORDER BY s.SortOrder, s.StartTime, s.Name;

        SELECT ss.Id AS Id, ss.MealSlotId AS MealSlotId, ss.Name AS Name, ss.DateFrom AS DateFrom, ss.DateTo AS DateTo
        FROM booking.MealSlotSeasons AS ss
        JOIN booking.MealSlots AS s ON s.Id = ss.MealSlotId
        WHERE s.InstitutionId = @institutionId AND s.IsActive = 1;

        SELECT b.Id AS Id, b.InstitutionId AS InstitutionId, b.MealSlotId AS MealSlotId,
               b.DateFrom AS DateFrom, b.DateTo AS DateTo, b.Reason AS Reason
        FROM booking.BlockedDates AS b
        WHERE b.InstitutionId = @institutionId AND b.DateFrom <= @last AND b.DateTo >= @first;

        SELECT si.Id AS Id, si.InstitutionId AS InstitutionId, si.MealSlotId AS MealSlotId, si.SlotDate AS SlotDate,
               si.MaxSponsors AS MaxSponsors, si.MaxHeadcount AS MaxHeadcount,
               si.BookedSponsors AS BookedSponsors, si.BookedHeadcount AS BookedHeadcount, si.Status AS Status
        FROM booking.SlotInstances AS si
        WHERE si.InstitutionId = @institutionId AND si.SlotDate >= @first AND si.SlotDate <= @last;
        """;

    private const string MineSql =
        """
        SELECT TOP (@max)
               b.BookingNo AS BookingNo, b.Status AS Status, si.SlotDate AS Date,
               CONVERT(CHAR(5), s.StartTime, 108) AS StartTime, CONVERT(CHAR(5), s.EndTime, 108) AS EndTime,
               i.Slug AS Institution, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, s.PublicId AS MealSlot, s.Name AS SlotName,
               s.NameSi AS SlotNameSi, s.NameTa AS SlotNameTa, b.Headcount AS Headcount,
               b.FulfilmentMode AS FulfilmentMode, b.InMemoryOf AS InMemoryOf, b.Notes AS Notes,
               b.CreatedAt AS CreatedAt, b.RejectReason AS RejectReason, b.CancelReason AS CancelReason
        FROM booking.Bookings AS b
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        JOIN booking.MealSlots AS s ON s.Id = si.MealSlotId AND s.InstitutionId = si.InstitutionId
        JOIN org.Institutions AS i ON i.Id = b.InstitutionId
        WHERE b.DonorUserId = @donorId
        ORDER BY si.SlotDate DESC, s.StartTime DESC, b.CreatedAt DESC;
        """;

    /// <summary><c>org.Institutions.Slug</c> is <c>VARCHAR(150)</c>; sent as such, the unique index is seeked, not converted.</summary>
    private const int SlugLength = 150;

    public async Task<IReadOnlyList<PublicInstitutionSummary>> ListInstitutionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PublicInstitutionSummary>(new CommandDefinition(
            ListSql,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<PublicInstitutionView?> GetInstitutionAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(
            ProfileSql,
            new { slug = Slug(slug) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var institution = await reader.ReadSingleOrDefaultAsync<ProfileRow>().ConfigureAwait(false);
        var slots = await reader.ReadAsync<PublicSlotRow>().ConfigureAwait(false);

        return institution is null
            ? null
            : new PublicInstitutionView(
                institution.Slug, institution.Name, institution.NameSi, institution.NameTa, institution.Type,
                institution.TypeName, institution.TypeNameSi, institution.TypeNameTa, institution.District,
                institution.DistrictSi, institution.DistrictTa, institution.City, institution.AddressLine1,
                institution.AddressLine2, institution.Description, institution.DietaryNotes,
                institution.Latitude, institution.Longitude,
                [.. slots.Select(r => new PublicMealSlotView(
                    r.PublicId, r.Name, r.NameSi, r.NameTa, r.StartTime, r.EndTime, r.DaysOfWeekMask,
                    r.BookingMode, r.MaxSponsors, r.MinHeadcount, r.MaxHeadcount, r.EstimatedCostPerHead,
                    r.MinAdvanceDays, r.MaxAdvanceDays, r.RequiresApproval, Fulfilment(r.AllowedFulfilment)))]);
    }

    public async Task<AvailabilitySource?> GetAvailabilitySourceAsync(string slug, DateOnly first, DateOnly last, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(
            AvailabilitySql,
            new { slug = Slug(slug), first, last },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var status = await reader.ReadSingleOrDefaultAsync<string>().ConfigureAwait(false);
        var slots = await reader.ReadAsync<SlotRuleRow>().ConfigureAwait(false);
        var seasons = await reader.ReadAsync<MealSlotSeason>().ConfigureAwait(false);
        var blocks = await reader.ReadAsync<BlockedDate>().ConfigureAwait(false);

        // Dapper sets the counters through their private setters; nothing here changes them (C2).
        var slotDays = await reader.ReadAsync<SlotInstance>().ConfigureAwait(false);

        return status is null
            ? null
            : new AvailabilitySource(status, [.. slots.Select(r => r.ToMealSlot())], [.. seasons], [.. blocks], [.. slotDays]);
    }

    public async Task<IReadOnlyList<DonorBookingView>> ListMineAsync(Guid donorId, DateOnly today, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MineRow>(new CommandDefinition(
            MineSql,
            new { donorId, max = IDonorBookingQueries.MaxMine },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new DonorBookingView(
            r.BookingNo, r.Status, r.Date, r.StartTime, r.EndTime, r.Institution, r.InstitutionName,
            r.InstitutionNameSi, r.InstitutionNameTa, r.MealSlot, r.SlotName, r.SlotNameSi, r.SlotNameTa,
            r.Headcount, r.FulfilmentMode, r.InMemoryOf, r.Notes, r.CreatedAt, r.RejectReason, r.CancelReason,
            Booking.DonorMayCancel(r.Status, r.Date, today)))];
    }

    private static DbString Slug(string slug) => new() { Value = slug.Trim(), IsAnsi = true, Length = SlugLength };

    private static string[] Fulfilment(string allowed) =>
        allowed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // Row classes rather than the records: Dapper's constructor matching does not apply the DateOnly handler.

    private sealed class ProfileRow
    {
        public string Slug { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string Type { get; init; } = null!;
        public string TypeName { get; init; } = null!;
        public string? TypeNameSi { get; init; }
        public string? TypeNameTa { get; init; }
        public string District { get; init; } = null!;
        public string? DistrictSi { get; init; }
        public string? DistrictTa { get; init; }
        public string City { get; init; } = null!;
        public string AddressLine1 { get; init; } = null!;
        public string? AddressLine2 { get; init; }
        public string? Description { get; init; }
        public string? DietaryNotes { get; init; }
        public decimal Latitude { get; init; }
        public decimal Longitude { get; init; }
    }

    private sealed class PublicSlotRow
    {
        public Guid PublicId { get; init; }
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string StartTime { get; init; } = null!;
        public string EndTime { get; init; } = null!;
        public byte DaysOfWeekMask { get; init; }
        public string BookingMode { get; init; } = null!;
        public byte MaxSponsors { get; init; }
        public short MinHeadcount { get; init; }
        public short MaxHeadcount { get; init; }
        public decimal? EstimatedCostPerHead { get; init; }
        public short MinAdvanceDays { get; init; }
        public short MaxAdvanceDays { get; init; }
        public bool RequiresApproval { get; init; }
        public string AllowedFulfilment { get; init; } = null!;
    }

    /// <summary>What <see cref="MealSlotRules"/> reads of a slot. <c>TIME</c> arrives as a <see cref="TimeSpan"/>.</summary>
    private sealed class SlotRuleRow
    {
        public int Id { get; init; }
        public Guid PublicId { get; init; }
        public int InstitutionId { get; init; }
        public string Name { get; init; } = null!;
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

    private sealed class MineRow
    {
        public string BookingNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateOnly Date { get; init; }
        public string StartTime { get; init; } = null!;
        public string EndTime { get; init; } = null!;
        public string Institution { get; init; } = null!;
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public Guid MealSlot { get; init; }
        public string SlotName { get; init; } = null!;
        public string? SlotNameSi { get; init; }
        public string? SlotNameTa { get; init; }
        public short Headcount { get; init; }
        public string FulfilmentMode { get; init; } = null!;
        public string? InMemoryOf { get; init; }
        public string? Notes { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? RejectReason { get; init; }
        public string? CancelReason { get; init; }
    }
}
