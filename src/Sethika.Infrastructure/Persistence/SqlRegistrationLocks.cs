using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary><see cref="IRegistrationLocks"/> on the context's own connection and transaction.</summary>
public sealed class SqlRegistrationLocks(SethikaDbContext context) : IRegistrationLocks
{
    /// <summary>The application lock every decision takes.</summary>
    public const string DecisionLock = "onboarding:decision";

    public async Task<bool> ClaimForDecisionAsync(string requestNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestNo);

        RequireTransaction();

        // sp_getapplock answers 0 or 1 when granted and a negative number otherwise (timeout,
        // deadlock victim, cancelled); a refusal must stop the decision, not be read as "granted".
        var found = await context.Database.SqlQuery<int>(
            $"""
            DECLARE @granted INT;
            EXEC @granted = sys.sp_getapplock @Resource = {DecisionLock}, @LockMode = N'Exclusive',
                                              @LockOwner = N'Transaction', @LockTimeout = 30000;
            IF @granted < 0
                THROW 51201, 'The onboarding decision lock was not granted.', 1;

            SELECT COUNT(*) AS Value
            FROM org.RegistrationRequests WITH (UPDLOCK)
            WHERE RequestNo = {requestNo};
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return found.Single() == 1;
    }

    public async Task LockApplicantAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();

        await context.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS Value FROM auth.Users WITH (UPDLOCK) WHERE Id = {userId};")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "The onboarding locks are held until the transaction that needs them ends; without one " +
                "they would be released before its work is saved.");
        }
    }
}
