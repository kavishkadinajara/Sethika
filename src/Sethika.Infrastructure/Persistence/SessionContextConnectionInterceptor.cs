using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// Stamps every EF Core connection with the tenant session context as it opens.
/// </summary>
/// <remarks>
/// Hooked on "opened" rather than "opening" because the stamp is itself a round trip
/// and needs a usable connection. ADO.NET issues sp_reset_connection when a pooled
/// connection is handed out again, which clears session context, so each logical open
/// gets a clean slate and is stamped afresh.
/// </remarks>
public sealed class SessionContextConnectionInterceptor(ICurrentUserContext currentUser) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SessionContext.Apply(connection, currentUser);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SessionContext.ApplyAsync(connection, currentUser, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
