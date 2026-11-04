using Dapper;
using Microsoft.Data.SqlClient;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Queries;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IDonorBookLinks"/>: linking at sign-up and sign-in, run as the platform (plan 08, K14, K36).
/// </summary>
/// <remarks>
/// <para>
/// The third narrow platform class, after <c>SqlShareLinks</c> (H33) and <c>SqlStandingHolds</c> (K25).
/// Its connections are stamped as the platform here, never from the request's user, and its one batch
/// is keyed by the signed-in person's id: the phone it matches is read from that account, and only
/// while the account's phone is confirmed and the account is not deleted. It returns a count, nothing
/// from any donor book.
/// </para>
/// <para>
/// A row is linked only while it has no account, and not in an institution where the person is
/// already another row's account (<c>UX_InstDonor_User</c>); a row staff linked stays as they left it.
/// The standing days already booked for a row it links (still to come: requested or approved) become
/// the person's, in the same transaction, so the person sees them in My bookings and gets the
/// day-before reminder; the job books the later ones with the account already (K21).
/// </para>
/// <para>
/// A staff member linking the same person to another row at the same moment can still win the index;
/// that one sign-in then links nothing, and the next one tries again.
/// </para>
/// </remarks>
public sealed class SqlDonorBookLinks(string connectionString) : IDonorBookLinks
{
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;

    private const string LinkSql =
        """
        SET XACT_ABORT ON;
        DECLARE @linked TABLE (Id INT NOT NULL, InstitutionId INT NOT NULL);

        BEGIN TRANSACTION;

        UPDATE d
        SET d.UserId = u.Id
        OUTPUT inserted.Id, inserted.InstitutionId INTO @linked (Id, InstitutionId)
        FROM org.InstitutionDonors AS d
        JOIN auth.Users AS u ON u.PhoneNumber = d.Phone
        WHERE u.Id = @userId AND u.PhoneConfirmed = 1 AND u.IsDeleted = 0
          AND d.UserId IS NULL
          AND NOT EXISTS (SELECT 1 FROM org.InstitutionDonors AS o WHERE o.InstitutionId = d.InstitutionId AND o.UserId = u.Id);

        UPDATE b
        SET b.DonorUserId = @userId
        FROM booking.Bookings AS b
        JOIN @linked AS l ON l.Id = b.InstitutionDonorId AND l.InstitutionId = b.InstitutionId
        WHERE b.Source = 'Standing' AND b.DonorUserId IS NULL AND b.Status IN ('Requested', 'Approved');

        COMMIT TRANSACTION;

        SELECT COUNT(*) AS Linked FROM @linked;
        """;

    private readonly SqlConnectionFactory _platform = new(connectionString, PlatformJobUser.Instance);

    public async Task<int> LinkAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _platform.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                LinkSql,
                new { userId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is DuplicateKey or UniqueConstraint)
        {
            return 0;
        }
    }
}
