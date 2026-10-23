using System.Data;
using Dapper;

namespace Sethika.Infrastructure.Queries;

/// <summary>
/// Reads a SQL <c>date</c> into <see cref="DateOnly"/> and writes one back. SqlClient hands a
/// <c>date</c> over as <see cref="DateTime"/>, and Dapper has no conversion of its own.
/// </summary>
/// <remarks>Dapper applies a handler for <c>T</c> to <c>T?</c> as well, nulls included.</remarks>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    private static int _registered;

    /// <summary>Registers the handler with Dapper once per process; later calls do nothing.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        }
    }

    public override DateOnly Parse(object value) =>
        value is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : (DateOnly)value;

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }
}
