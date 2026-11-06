using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Core;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Payments;

/// <summary>
/// An institution's own merchant settings (plan 09, M10). Runs in the owner's session, so Row-Level
/// Security keeps it to their institution, and the permission behind it needs a step-up.
/// </summary>
/// <remarks>
/// The secret is write-only: it comes in here, is sealed (M21), and nothing reads it back but a payment.
/// A PayHere secret belongs to one merchant, so changing the merchant id asks for its secret too: keeping
/// the old one would make every checkout hash wrong. The audit row says that the secret changed, never what to.
/// </remarks>
public sealed class PaymentMerchantHandler(ISethikaDbContext db, IMerchantSecrets secrets, TimeProvider timeProvider)
{
    /// <summary>The audit rows' entity.</summary>
    public const string Aggregate = "PaymentMerchant";

    /// <summary>Creates or changes the institution's merchant.</summary>
    public async Task<MerchantResult> SaveAsync(int institutionId, Guid userId, MerchantInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var merchantId = input.MerchantId?.Trim();
        var secret = string.IsNullOrWhiteSpace(input.Secret) ? null : input.Secret.Trim();

        var merchant = await db.PaymentMerchants
            .SingleOrDefaultAsync(m => m.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        var invalid = new List<string>();
        if (!IsMerchantId(merchantId))
        {
            invalid.Add("merchantId");
        }

        if (secret is { Length: > PaymentMerchant.MaxSecret } || (secret is null && (merchant is null || merchant.MerchantId != merchantId)))
        {
            invalid.Add("secret");
        }

        if (merchant is not null && input.Version is not { Length: 8 })
        {
            invalid.Add("version");
        }

        if (invalid.Count > 0)
        {
            return MerchantResult.Invalid(invalid);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = merchant is null;

        if (merchant is null)
        {
            merchant = new PaymentMerchant { InstitutionId = institutionId };
            db.PaymentMerchants.Add(merchant);
        }
        else
        {
            db.Entry(merchant).Property(m => m.RowVersion).OriginalValue = input.Version!;
        }

        merchant.MerchantId = merchantId!;
        merchant.IsEnabled = input.Enabled;
        merchant.UpdatedBy = userId;
        merchant.UpdatedAt = now;

        if (secret is not null)
        {
            var sealedSecret = secrets.Seal(institutionId, secret);
            merchant.SecretKeyId = sealedSecret.KeyId;
            merchant.SecretNonce = sealedSecret.Nonce;
            merchant.SecretCipher = sealedSecret.Cipher;
        }

        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = userId,
            InstitutionId = institutionId,
            Action = created ? "Create" : "Update",
            EntityName = Aggregate,
            EntityId = merchant.MerchantId,
            NewValues = JsonSerializer.Serialize(new { merchantId = merchant.MerchantId, enabled = merchant.IsEnabled, secretChanged = secret is not null }),
            OccurredAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MerchantResult.Failed(MerchantResult.Stale);
        }
        catch (DuplicateKeyException)
        {
            // Two first saves at once: UX_PayMerchant_Institution let one through.
            return MerchantResult.Failed(MerchantResult.Stale);
        }

        return MerchantResult.For(merchant.RowVersion);
    }

    /// <summary>Letters and digits, as PayHere's merchant ids are, up to <c>VARCHAR(30)</c>.</summary>
    private static bool IsMerchantId(string? value) =>
        value is { Length: > 0 and <= PaymentMerchant.MaxMerchantId } && value.All(char.IsAsciiLetterOrDigit);
}
