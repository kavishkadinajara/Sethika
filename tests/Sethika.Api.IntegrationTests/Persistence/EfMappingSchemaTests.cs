using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// EF Core maps entities onto tables the SQL project owns (CLAUDE.md: no migrations), so nothing but
/// a test notices when a mapping drifts from the schema. Each mapped column must exist, and where a
/// configuration names a column type, it must be the published one.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class EfMappingSchemaTests(SethikaTestDatabase database)
{
    private sealed record Column(string Schema, string Table, string Name, string Type);

    private async Task<Dictionary<(string, string, string), string>> PublishedColumnsAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);

        // The type as a configuration would write it: nvarchar(100), decimal(10,2), time(0), date.
        var columns = await connection.QueryAsync<Column>(
            """
            SELECT s.name AS [Schema], o.name AS [Table], c.name AS Name,
                   t.name + CASE
                       WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(VARCHAR(5), c.max_length) END + ')'
                       WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(VARCHAR(5), c.max_length / 2) END + ')'
                       WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CONVERT(VARCHAR(3), c.precision) + ',' + CONVERT(VARCHAR(3), c.scale) + ')'
                       WHEN t.name IN ('datetime2', 'time', 'datetimeoffset')
                           THEN '(' + CONVERT(VARCHAR(3), c.scale) + ')'
                       ELSE '' END AS Type
            FROM sys.columns c
            JOIN sys.objects o ON o.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.types t   ON t.user_type_id = c.user_type_id
            WHERE o.type IN ('U', 'V');
            """);

        return columns.ToDictionary(c => (c.Schema, c.Table, c.Name), c => c.Type);
    }

    private static IModel Model()
    {
        using var context = new SethikaDbContext(
            new DbContextOptionsBuilder<SethikaDbContext>().UseSqlServer("Server=unused").Options);
        return context.Model;
    }

    [Fact]
    public async Task EveryMappedColumnExistsWithTheTypeItsConfigurationNames()
    {
        var published = await PublishedColumnsAsync();
        var problems = new List<string>();

        foreach (var entity in Model().GetEntityTypes())
        {
            var table = entity.GetTableName() ?? entity.GetViewName();
            var schema = entity.GetSchema() ?? entity.GetViewSchema() ?? "dbo";
            if (table is null)
            {
                continue;
            }

            var store = entity.GetTableName() is null
                ? StoreObjectIdentifier.View(table, schema)
                : StoreObjectIdentifier.Table(table, schema);

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(store);
                if (column is null)
                {
                    continue;
                }

                if (!published.TryGetValue((schema, table, column), out var type))
                {
                    problems.Add($"{schema}.{table}.{column} ({entity.ClrType.Name}.{property.Name}) is not in the database");
                    continue;
                }

                // Only a type the configuration wrote itself: an inferred one is EF's guess, not a claim.
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value is string configured
                    && !string.Equals(configured, type, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{schema}.{table}.{column} is {type}, mapped as {configured}");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void TheBookingTablesAreMapped()
    {
        // The check above passes vacuously for a table nobody maps; plan 03's must be among them.
        var tables = Model().GetEntityTypes().Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToHashSet();

        Assert.Superset(
            new HashSet<string>
            {
                "booking.MealSlots", "booking.MealSlotSeasons", "booking.BlockedDates",
                "booking.SlotInstances", "booking.Bookings", "booking.BookingStatusHistory",
            },
            tables);
    }

    [Fact]
    public void TheReligiousServiceTablesAreMapped()
    {
        // Phase E's eight tables, for the same reason.
        var tables = Model().GetEntityTypes().Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToHashSet();

        Assert.Superset(
            new HashSet<string>
            {
                "org.ReligiousLeaders", "org.LeaderWeeklyHours", "org.LeaderTimeOff",
                "booking.ServiceTypes", "booking.ServiceAppointments", "booking.AppointmentLeaders",
                "booking.LeaderTimeBlocks", "booking.AppointmentStatusHistory",
            },
            tables);
    }
}
