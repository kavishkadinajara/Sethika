using System.Globalization;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Reports;
using Sethika.Domain.Bookings;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The monthly report and the Day Ledger's giving line (plan 09, D.4). Each acts for the institution in
/// the caller's token. The file has a route of its own, rather than <c>?format=csv</c>, so that
/// <c>GIVING.REPORTS.EXPORT</c> is the endpoint's policy and not a check inside a handler (M38).
/// </summary>
internal static class ReportEndpoints
{
    private const string ReportView = "GIVING.REPORTS.VIEW";
    private const string ReportExport = "GIVING.REPORTS.EXPORT";
    private const string DonationsView = "GIVING.DONATIONS.VIEW";

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/institution/reports");

        reports.MapGet("/monthly", MonthlyAsync).WithMetadata(new HasPermissionAttribute(ReportView));
        reports.MapGet("/monthly/csv", MonthlyCsvAsync).WithMetadata(new HasPermissionAttribute(ReportExport));

        app.MapGet("/api/institution/giving/summary", SummaryAsync).WithMetadata(new HasPermissionAttribute(DonationsView));

        return app;
    }

    private static async Task<IResult> MonthlyAsync(string? month, ICurrentUserContext currentUser, MonthlyReportHandler handler, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        if (!ReportMonth.TryRead(month, timeProvider, out var first))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, ReportMonth.Invalid);
        }

        return await handler.GetAsync(institutionId, first, cancellationToken) is { } report
            ? Results.Ok(report)
            : NoInstitution();
    }

    private static async Task<IResult> MonthlyCsvAsync(string? month, string? lang, HttpContext httpContext, ICurrentUserContext currentUser, MonthlyReportHandler handler, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        if (!ReportMonth.TryRead(month, timeProvider, out var first))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, ReportMonth.Invalid);
        }

        return await handler.GetAsync(institutionId, first, cancellationToken) is { } report
            ? FileResults.Csv(httpContext, ReportCsv.Monthly(report, lang), $"sethika-report-{report.Month}.csv")
            : NoInstitution();
    }

    private static async Task<IResult> SummaryAsync(ICurrentUserContext currentUser, IReportQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        return Results.Ok(await queries.GetGivingSummaryAsync(institutionId, today, cancellationToken));
    }

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");
}

/// <summary>A <c>month</c> query parameter, shared by the lists and the report.</summary>
internal static class ReportMonth
{
    public const string Invalid = "invalid_month";

    /// <summary>Beyond these the month's edges cannot all be turned into UTC instants, and nothing is there anyway.</summary>
    private const int FirstYear = 2000;

    private const int LastYear = 2100;

    /// <summary>The first day of <c>yyyy-MM</c>, or of this Colombo month when left out.</summary>
    public static bool TryRead(string? month, TimeProvider timeProvider, out DateOnly first)
    {
        if (string.IsNullOrEmpty(month))
        {
            var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
            first = new DateOnly(today.Year, today.Month, 1);
            return true;
        }

        return DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first)
               && first.Year is >= FirstYear and <= LastYear;
    }
}
