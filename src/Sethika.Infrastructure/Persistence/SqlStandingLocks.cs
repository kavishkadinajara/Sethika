using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary><see cref="IStandingLocks"/> on the context's own connection and transaction.</summary>
public sealed class SqlStandingLocks(SethikaDbContext context) : IStandingLocks
{
    public async Task<bool> LockActiveAsync(int sponsorshipId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A sponsorship's row lock is held until the transaction that needs it ends; without one it " +
                "would be released before its work is saved.");
        }

        // The status is read under the lock, so it is the one a change that held the lock committed.
        var active = await context.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS Value FROM booking.StandingSponsorships WITH (UPDLOCK, ROWLOCK) WHERE Id = {sponsorshipId} AND Status = 'Active';")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return active.Single() == 1;
    }
}
