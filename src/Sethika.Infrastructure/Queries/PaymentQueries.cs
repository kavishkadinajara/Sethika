using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for online giving (plan 09, D.3): the merchant settings and the return page.</summary>
/// <remarks>
/// No query here selects <c>SecretCipher</c>, <c>SecretNonce</c> or <c>SecretKeyId</c>: the secret is
/// write-only (M10), and only <c>OnlinePaymentHandler</c> reads it, to sign.
/// </remarks>
public sealed class PaymentQueries(IDbConnectionFactory connections) : IPaymentQueries
{
    private const string MerchantSql =
        """
        SELECT m.MerchantId AS MerchantId, m.IsEnabled AS IsEnabled, m.UpdatedAt AS UpdatedAt,
               u.FullName AS UpdatedByName, m.RowVersion AS Version
        FROM giving.PaymentMerchants AS m
        JOIN auth.Users AS u ON u.Id = m.UpdatedBy
        WHERE m.InstitutionId = @institutionId;
        """;

    private const string PaymentSql =
        """
        SELECT p.OrderId AS OrderId, p.Amount AS Amount, p.CurrencyCode AS CurrencyCode, p.Status AS Status,
               i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi, i.NameTa AS InstitutionNameTa,
               i.Slug AS InstitutionSlug, d.PublicId AS ReceiptPublicId, d.ReceiptNo AS ReceiptNo
        FROM giving.OnlinePayments AS p
        JOIN org.Institutions AS i ON i.Id = p.InstitutionId
        LEFT JOIN giving.Donations AS d ON d.Id = p.DonationId
        WHERE p.OrderId = @orderId AND p.DonorUserId = @userId;
        """;

    public async Task<MerchantSettings?> GetMerchantAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<MerchantSettings>(new CommandDefinition(
            MerchantSql,
            new { institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<MyPayment?> GetMyPaymentAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<MyPayment>(new CommandDefinition(
            PaymentSql,
            new { userId, orderId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
