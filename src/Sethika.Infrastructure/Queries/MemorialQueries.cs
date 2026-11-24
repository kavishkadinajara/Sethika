using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the Memorial Dana Planner (plan 07, H6, H8).</summary>
/// <remarks>
/// A plan is the donor's alone under Row-Level Security (<c>sec.fn_Owner</c>, H13), and the donor's
/// reads name the donor as well, as <see cref="DonorBookingQueries"/> does: RLS decides who may see a
/// plan, the query which plans this screen is about. The reminder job reads every plan as the platform.
/// </remarks>
public sealed class MemorialQueries(IDbConnectionFactory connections) : IMemorialQueries
{
    private const int CodeLength = 50;

    private const string ReligionsSql =
        """
        SELECT v.Code AS Code, v.Name AS Name, v.NameSi AS NameSi, v.NameTa AS NameTa
        FROM core.LookupValues AS v
        JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId
        WHERE t.Code = 'RELIGION' AND v.IsActive = 1
          AND EXISTS (SELECT 1 FROM core.MemorialRules AS r WHERE r.ReligionLookupId = v.Id AND r.IsActive = 1)
        ORDER BY v.SortOrder, v.Code;

        SELECT v.Code AS Religion, r.Code AS Code, r.Name AS Name, r.NameSi AS NameSi, r.NameTa AS NameTa,
               r.OffsetMonths AS OffsetMonths, r.OffsetDays AS OffsetDays, r.RepeatsAnnually AS RepeatsAnnually
        FROM core.MemorialRules AS r
        JOIN core.LookupValues AS v ON v.Id = r.ReligionLookupId
        WHERE r.IsActive = 1
        ORDER BY r.SortOrder, r.Code;
        """;

    private const string RelationshipsSql =
        """
        SELECT v.Code AS Code, v.Name AS Name, v.NameSi AS NameSi, v.NameTa AS NameTa
        FROM core.LookupValues AS v
        JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId
        WHERE t.Code = 'RELATIONSHIP' AND v.IsActive = 1
        ORDER BY v.SortOrder, v.Code;
        """;

    private const string ResolveSql =
        """
        DECLARE @religionId INT =
            (SELECT v.Id FROM core.LookupValues AS v JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId
             WHERE t.Code = 'RELIGION' AND v.Code = @religion AND v.IsActive = 1);

        SELECT @religionId AS Religion,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM core.MemorialRules AS r WHERE r.ReligionLookupId = @religionId AND r.IsActive = 1)
                         THEN 1 ELSE 0 END AS BIT) AS ReligionHasRules,
               (SELECT v.Id FROM core.LookupValues AS v JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId
                WHERE t.Code = 'RELATIONSHIP' AND v.Code = @relationship AND v.IsActive = 1) AS Relationship;
        """;

    private const string RulesSql =
        """
        SELECT r.Id AS Id, r.ReligionLookupId AS ReligionLookupId, r.Code AS Code, r.Name AS Name, r.NameSi AS NameSi,
               r.NameTa AS NameTa, r.OffsetDays AS OffsetDays, r.OffsetMonths AS OffsetMonths,
               r.RepeatsAnnually AS RepeatsAnnually, r.SortOrder AS SortOrder, r.IsActive AS IsActive
        FROM core.MemorialRules AS r
        WHERE r.IsActive = 1;
        """;

    private const string PlanColumns =
        """
        p.Id AS Id, p.PublicId AS PublicId, p.DonorUserId AS DonorUserId, p.DeceasedName AS DeceasedName,
        rel.Code AS Relationship, p.DateOfPassing AS DateOfPassing, p.ReligionLookupId AS ReligionLookupId,
        rv.Code AS Religion, p.Notes AS Notes, p.CreatedAt AS CreatedAt, u.PreferredLanguage AS Language
        """;

    private const string PlanJoins =
        """
        JOIN core.LookupValues AS rv ON rv.Id = p.ReligionLookupId
        LEFT JOIN core.LookupValues AS rel ON rel.Id = p.RelationshipLookupId
        JOIN auth.Users AS u ON u.Id = p.DonorUserId
        """;

    private const string BookingColumns =
        """
        b.MemorialPlanId AS MemorialPlanId, si.SlotDate AS Date, b.BookingNo AS BookingNo, b.Status AS Status,
        i.Slug AS Institution, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi, i.NameTa AS InstitutionNameTa
        """;

    private const string BookingJoins =
        """
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId
        JOIN org.Institutions AS i ON i.Id = b.InstitutionId
        """;

    // The donor's own plans: @plan narrows them to one.
    private const string MineSql =
        $"""
        SELECT {PlanColumns}
        FROM booking.MemorialPlans AS p
        {PlanJoins}
        WHERE p.DonorUserId = @donorId AND p.IsActive = 1 AND (@plan IS NULL OR p.PublicId = @plan);

        {RulesSql}

        SELECT {BookingColumns}
        FROM booking.Bookings AS b
        JOIN booking.MemorialPlans AS p ON p.Id = b.MemorialPlanId
        {BookingJoins}
        WHERE b.DonorUserId = @donorId AND p.DonorUserId = @donorId AND p.IsActive = 1
          AND (@plan IS NULL OR p.PublicId = @plan) AND b.Status IN ('Requested', 'Approved');

        SELECT r.MemorialPlanId AS MemorialPlanId, r.RecurMonth AS Month, r.RecurDay AS Day, r.PublicId AS PublicId
        FROM booking.RecurringBookings AS r
        JOIN booking.MemorialPlans AS p ON p.Id = r.MemorialPlanId
        WHERE r.DonorUserId = @donorId AND r.IsActive = 1 AND p.DonorUserId = @donorId AND p.IsActive = 1
          AND (@plan IS NULL OR p.PublicId = @plan);
        """;

    // Every active plan, as the platform: the reminder job.
    private const string AllSql =
        $"""
        SELECT {PlanColumns}
        FROM booking.MemorialPlans AS p
        {PlanJoins}
        WHERE p.IsActive = 1;

        {RulesSql}

        SELECT {BookingColumns}
        FROM booking.Bookings AS b
        JOIN booking.MemorialPlans AS p ON p.Id = b.MemorialPlanId
        {BookingJoins}
        WHERE p.IsActive = 1 AND b.Status IN ('Requested', 'Approved') AND si.SlotDate >= @from;
        """;

    public async Task<IReadOnlyList<MemorialReligionView>> ListReligionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ReligionsSql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var religions = (await grid.ReadAsync<ChoiceRow>().ConfigureAwait(false)).AsList();
        var rules = (await grid.ReadAsync<RuleRow>().ConfigureAwait(false)).ToLookup(r => r.Religion, StringComparer.Ordinal);

        return [.. religions.Select(r => new MemorialReligionView(
            r.Code, r.Name, r.NameSi, r.NameTa,
            [.. rules[r.Code].Select(x => new MemorialRuleView(x.Code, x.Name, x.NameSi, x.NameTa, x.OffsetMonths, x.OffsetDays, x.RepeatsAnnually))]))];
    }

    public async Task<IReadOnlyList<MemorialChoice>> ListRelationshipsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ChoiceRow>(new CommandDefinition(RelationshipsSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows.Select(r => new MemorialChoice(r.Code, r.Name, r.NameSi, r.NameTa))];
    }

    public async Task<MemorialLookups> ResolveAsync(string religion, string? relationship, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleAsync<LookupsRow>(new CommandDefinition(
            ResolveSql,
            new { religion = Code(religion), relationship = Code(relationship) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new MemorialLookups(row.Religion, row.ReligionHasRules, row.Relationship);
    }

    public async Task<MemorialPlansData> LoadAsync(Guid donorId, Guid? plan, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            MineSql,
            new { donorId, plan },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var plans = (await grid.ReadAsync<MemorialPlanRow>().ConfigureAwait(false)).AsList();
        var rules = (await grid.ReadAsync<MemorialRule>().ConfigureAwait(false)).AsList();
        var bookings = (await grid.ReadAsync<MemorialBookingRow>().ConfigureAwait(false)).AsList();
        var yearly = (await grid.ReadAsync<MemorialYearlyRow>().ConfigureAwait(false)).AsList();
        return new MemorialPlansData(plans, rules, bookings, yearly);
    }

    public async Task<MemorialPlansData> LoadAllAsync(DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            AllSql,
            new { from },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var plans = (await grid.ReadAsync<MemorialPlanRow>().ConfigureAwait(false)).AsList();
        var rules = (await grid.ReadAsync<MemorialRule>().ConfigureAwait(false)).AsList();
        var bookings = (await grid.ReadAsync<MemorialBookingRow>().ConfigureAwait(false)).AsList();
        return new MemorialPlansData(plans, rules, bookings, []);
    }

    private static DbString Code(string? code) => new() { Value = code?.Trim(), IsAnsi = true, Length = CodeLength };

    private sealed class ChoiceRow
    {
        public string Code { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
    }

    private sealed class RuleRow
    {
        public string Religion { get; init; } = null!;
        public string Code { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public short OffsetMonths { get; init; }
        public short OffsetDays { get; init; }
        public bool RepeatsAnnually { get; init; }
    }

    private sealed class LookupsRow
    {
        public int? Religion { get; init; }
        public bool ReligionHasRules { get; init; }
        public int? Relationship { get; init; }
    }
}
