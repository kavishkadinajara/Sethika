using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="INumberSeries"/> on the context's own connection and transaction: make sure the series
/// row exists, then increment it.
/// </summary>
/// <remarks>
/// No range locks, on purpose. A <c>MERGE ... WITH (HOLDLOCK)</c> was the first version, and callers
/// racing to create a new series (the first number ever, or the first of a new year) deadlocked on
/// their key-range locks, with UPDLOCK added too (plan 02, 5.2). Here the only lock is on the series
/// row itself:
/// <list type="number">
/// <item>Insert the row at 0 if it is missing. Two callers that both find it missing both insert; the
/// second waits on the first's key lock and, once the first commits, fails on the primary key (2627),
/// which the CATCH swallows - the row now exists, which is all this step is for. The CATCH is the
/// guarantee; <c>NOT EXISTS</c> only keeps the ordinary call from raising an error every time.</item>
/// <item>Increment it. The UPDATE's exclusive row lock is held to the end of the caller's
/// transaction, which serialises every later caller and makes a rollback hand the number back - the
/// insert of a new row rolls back with it.</item>
/// </list>
/// A 2627 caught inside a transaction does not doom it while XACT_ABORT is off, which is SQL
/// Server's default and how EF Core leaves the connection.
/// </remarks>
public sealed class SqlNumberSeries(SethikaDbContext context) : INumberSeries
{
    public async Task<string> NextAsync(
        NumberSeriesDefinition series,
        string scopeKey,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);

        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A number is taken inside the transaction that saves what it numbers. Without one, a " +
                "failed save would still spend the number, and the series would no longer be gap-free.");
        }

        var seriesYear = checked((short)year);

        var rows = await context.Database.SqlQuery<IssuedNumber>(
            $"""
            BEGIN TRY
                INSERT core.NumberSeries (SeriesCode, ScopeKey, SeriesYear, Prefix, LastNumber, PadLength)
                SELECT {series.Code}, {scopeKey}, {seriesYear}, {series.Prefix}, 0, {series.PadLength}
                WHERE NOT EXISTS (SELECT 1 FROM core.NumberSeries
                                  WHERE SeriesCode = {series.Code} AND ScopeKey = {scopeKey} AND SeriesYear = {seriesYear});
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2627 THROW;
            END CATCH;

            UPDATE core.NumberSeries
            SET LastNumber = LastNumber + 1
            OUTPUT inserted.Prefix AS Prefix, inserted.SeriesYear AS SeriesYear,
                   inserted.LastNumber AS LastNumber, inserted.PadLength AS PadLength
            WHERE SeriesCode = {series.Code} AND ScopeKey = {scopeKey} AND SeriesYear = {seriesYear};
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        var issued = rows.Single();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{issued.Prefix}-{issued.SeriesYear}-{issued.LastNumber.ToString(new string('0', issued.PadLength), CultureInfo.InvariantCulture)}");
    }

    /// <summary>The row as the increment left it.</summary>
    private sealed record IssuedNumber(string Prefix, short SeriesYear, int LastNumber, byte PadLength);
}
