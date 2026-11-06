using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Donations;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Payments;

/// <summary>
/// Giving online through the institution's own PayHere merchant (plan 09, M8-M10): the order and its
/// checkout form, and the signed notification that alone turns an order into a donation.
/// </summary>
/// <remarks>
/// <para>
/// Both run as the platform (<see cref="IPlatformPayments"/>): the order reads a merchant a donor's session
/// may not see, and the notification has no session at all. So nothing here leans on Row-Level Security.
/// <see cref="StartAsync"/> takes only the caller's id and the address they named, and writes the order
/// with <c>DonorUserId</c> = the caller. <see cref="NotifyAsync"/> believes nothing it was sent until the
/// signature made with that institution's own secret has been checked.
/// </para>
/// <para>
/// PayHere has already taken the money by the time a success arrives, so nothing about the institution or
/// the need can refuse it then: not a merchant since disabled, not a need since closed.
/// </para>
/// </remarks>
public sealed partial class OnlinePaymentHandler(
    ISethikaDbContext db,
    INumberSeries numbers,
    IMerchantSecrets secrets,
    IPayHereSite site,
    TimeProvider timeProvider,
    ILogger<OnlinePaymentHandler> logger)
{
    /// <summary>The <c>PAYMENT_METHOD</c> value of a gift made online: set here only, never chosen by staff (M9).</summary>
    public const string MethodCode = "PAYHERE";

    /// <summary>An address a person types; PayHere states no limit, so this only keeps the form sane.</summary>
    private const int MaxAddress = 200;

    private const int MaxCity = 100;

    /// <summary><c>auth.Users.Email</c>'s width.</summary>
    private const int MaxEmail = 256;

    /// <summary>PayHere asks for a country; Sethika is a platform for Sri Lanka, and asking would add nothing.</summary>
    private const string Country = "Sri Lanka";

    /// <summary><c>org.Institutions.Status</c> of one that takes gifts.</summary>
    private const string Active = "Active";

    /// <summary>The caller starts a gift to the institution <paramref name="slug"/>.</summary>
    public async Task<GiveResult> StartAsync(Guid userId, string slug, GiveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var email = input.Email?.Trim();
        var address = input.Address?.Trim();
        var city = input.City?.Trim();

        var invalid = new List<string>();
        if (input.Amount is not { } amount || amount < OnlinePayment.MinAmount || amount > OnlinePayment.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            invalid.Add("amount");
        }

        if (!IsEmail(email))
        {
            invalid.Add("email");
        }

        if (string.IsNullOrEmpty(address) || address.Length > MaxAddress)
        {
            invalid.Add("address");
        }

        if (string.IsNullOrEmpty(city) || city.Length > MaxCity)
        {
            invalid.Add("city");
        }

        if (invalid.Count > 0)
        {
            return GiveResult.Invalid(invalid);
        }

        var donor = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.PhoneConfirmed && u.PhoneNumber != null)
            .Select(u => new { u.FullName, u.PhoneNumber, u.PreferredLanguage })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (donor is null)
        {
            return GiveResult.Failed(GiveResult.PhoneRequired);
        }

        // As the platform this reads every institution, so what takes gifts is decided here.
        var institution = await db.Institutions
            .AsNoTracking()
            .Where(i => i.Slug == slug && i.Status == Active && !i.IsDeleted)
            .Select(i => new { i.Id, i.Name })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (institution is null)
        {
            return GiveResult.Failed(GiveResult.InstitutionNotFound);
        }

        var merchant = await db.PaymentMerchants
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.InstitutionId == institution.Id && m.IsEnabled, cancellationToken)
            .ConfigureAwait(false);

        if (merchant is null)
        {
            return GiveResult.Failed(GiveResult.OnlineGivingOff);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        int? needId = null;
        if (input.Need is { } needPublicId)
        {
            var need = await db.Needs
                .AsNoTracking()
                .Where(n => n.PublicId == needPublicId && n.InstitutionId == institution.Id && n.Status != NeedStatus.Draft)
                .Select(n => new { n.Id, n.Status, n.Deadline })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (need is null)
            {
                return GiveResult.Failed(NeedErrors.NotFound);
            }

            if (need.Status != NeedStatus.Open || need.Deadline < SriLankaTime.Today(now))
            {
                return GiveResult.Failed(NeedErrors.Closed);
            }

            needId = need.Id;
        }

        // Before the order is written: a secret that cannot be opened must not leave a pending row behind.
        var secret = secrets.Open(institution.Id, Sealed(merchant));

        var order = new OnlinePayment
        {
            OrderId = Guid.CreateVersion7(now),
            InstitutionId = institution.Id,
            DonorUserId = userId,
            MerchantId = merchant.MerchantId,
            Amount = input.Amount!.Value,
            NeedId = needId,
            CreatedAt = now,
        };

        db.OnlinePayments.Add(order);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var orderId = order.OrderId.ToString();
        var (firstName, lastName) = SplitName(donor.FullName);

        return GiveResult.For(new CheckoutForm(order.OrderId, site.CheckoutUrl, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["merchant_id"] = merchant.MerchantId,
            ["return_url"] = site.ReturnUrl(order.OrderId, donor.PreferredLanguage),
            ["cancel_url"] = site.CancelUrl(order.OrderId, donor.PreferredLanguage),
            ["notify_url"] = site.NotifyUrl,
            ["first_name"] = firstName,
            ["last_name"] = lastName,
            ["email"] = email!,
            ["phone"] = donor.PhoneNumber!,
            ["address"] = address!,
            ["city"] = city!,
            ["country"] = Country,
            ["order_id"] = orderId,
            ["items"] = $"Donation to {institution.Name}",
            ["currency"] = order.CurrencyCode,
            ["amount"] = PayHereSignature.Amount(order.Amount),
            ["hash"] = PayHereSignature.Checkout(merchant.MerchantId, orderId, order.Amount, order.CurrencyCode, secret),
        }));
    }

    /// <summary>Applies PayHere's notification, if it is genuine.</summary>
    /// <returns>
    /// <see langword="false"/> when it is refused. A repeat, a late one, or a status that changes nothing is
    /// genuine and answers <see langword="true"/>.
    /// </returns>
    public async Task<bool> NotifyAsync(PayHereNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification is not { MerchantId: { } merchantId, OrderId: { } orderText, Amount: { } amountText, Currency: { } currency, StatusCode: { } statusText }
            || !Guid.TryParse(orderText, out var orderId))
        {
            return Refuse("incomplete", notification.OrderId);
        }

        var order = await db.OnlinePayments
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrderId == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Refuse("unknown order", orderText);
        }

        if (!string.Equals(merchantId, order.MerchantId, StringComparison.Ordinal))
        {
            return Refuse("another merchant", orderText);
        }

        // Disabled or not: an order made while it was on is still answered for. A merchant changed since
        // has another secret, so an order of the old one cannot be checked and stays pending.
        var merchant = await db.PaymentMerchants
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.InstitutionId == order.InstitutionId && m.MerchantId == order.MerchantId, cancellationToken)
            .ConfigureAwait(false);

        if (merchant is null)
        {
            return Refuse("merchant gone", orderText);
        }

        var secret = secrets.Open(order.InstitutionId, Sealed(merchant));
        var expected = PayHereSignature.Notification(merchantId, orderText, amountText, currency, statusText, secret);
        if (!PayHereSignature.Matches(expected, notification.Signature))
        {
            return Refuse("bad signature", orderText);
        }

        // Signed, so from PayHere; what it says must still be what was ordered.
        if (!decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount != order.Amount)
        {
            return Refuse("wrong amount", orderText);
        }

        if (!string.Equals(currency, order.CurrencyCode, StringComparison.Ordinal))
        {
            return Refuse("wrong currency", orderText);
        }

        if (!short.TryParse(statusText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var status))
        {
            return Refuse("unreadable status", orderText);
        }

        var paymentId = notification.PaymentId is { Length: > OnlinePayment.MaxPaymentId } tooLong
            ? tooLong[..OnlinePayment.MaxPaymentId]
            : notification.PaymentId;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        switch (status)
        {
            case PayHereStatus.Success:
                await RecordGiftAsync(order, paymentId, now, cancellationToken).ConfigureAwait(false);
                break;
            case PayHereStatus.Cancelled:
                await CloseAsync(orderId, OnlinePaymentStatus.Cancelled, status, paymentId, now, cancellationToken).ConfigureAwait(false);
                break;
            case PayHereStatus.Failed or PayHereStatus.ChargedBack:
                await CloseAsync(orderId, OnlinePaymentStatus.Failed, status, paymentId, now, cancellationToken).ConfigureAwait(false);
                break;
            default:
                // 0 is "still pending"; anything else is not PayHere's documented list. Neither moves the order.
                break;
        }

        return true;
    }

    /// <summary>
    /// One transaction (M9): claims the order, draws the receipt number, writes the gift, the audit row and
    /// the receipt text, then marks it paid. <c>CK_OnlinePay_Paid</c> ties Paid to a donation, so the claim
    /// is its own conditional UPDATE first: its row lock makes a second notification wait, find the order
    /// paid, and stop before it has taken a number.
    /// </summary>
    private async Task RecordGiftAsync(OnlinePayment order, string? paymentId, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Not "still pending": a signed success after a failure means the money was taken on a retry (M32).
        var claimed = await db.OnlinePayments
            .Where(p => p.Id == order.Id && p.Status != OnlinePaymentStatus.Paid)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(p => p.PayHereStatusCode, PayHereStatus.Success)
                    .SetProperty(p => p.PayHerePaymentId, paymentId)
                    .SetProperty(p => p.NotifiedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return;
        }

        var donor = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == order.DonorUserId)
            .Select(u => new { u.FullName, u.PhoneNumber })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var scope = await db.Institutions
            .Where(i => i.Id == order.InstitutionId)
            .Select(i => i.InstitutionCode)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var methodId = await db.LookupValues
            .Where(v => v.Code == MethodCode)
            .Join(db.LookupTypes.Where(t => t.Code == LookupValue.PaymentMethod), v => v.LookupTypeId, t => t.Id, (v, t) => v.Id)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var donation = new Donation
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = order.InstitutionId,
            DonorUserId = order.DonorUserId,
            DonorName = donor.FullName,
            DonorPhone = donor.PhoneNumber,
            Kind = Donation.Money,
            Amount = order.Amount,
            CurrencyCode = order.CurrencyCode,
            PaymentMethodLookupId = methodId,
            NeedId = order.NeedId,
            DonatedAt = now,
            RecordedBy = order.DonorUserId,
            RecordedAt = now,
        };

        donation.ReceiptNo = await numbers
            .NextAsync(NumberSeriesDefinition.Receipt, scope, SriLankaTime.Today(now).Year, cancellationToken)
            .ConfigureAwait(false);

        db.Donations.Add(donation);
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = order.DonorUserId,
            InstitutionId = order.InstitutionId,
            Action = "Record",
            EntityName = DonationHandler.Aggregate,
            EntityId = donation.ReceiptNo,
            // Codes and figures only: no names or phones (the receipt holds those).
            NewValues = JsonSerializer.Serialize(new
            {
                publicId = donation.PublicId,
                kind = donation.Kind,
                amount = donation.Amount,
                method = MethodCode,
                orderId = order.OrderId,
                paymentId,
            }),
            OccurredAt = now,
        });
        db.OutboxMessages.Add(DonationTexts.Message(donation.PublicId, now));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await db.OnlinePayments
            .Where(p => p.Id == order.Id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(p => p.Status, OnlinePaymentStatus.Paid)
                    .SetProperty(p => p.DonationId, donation.Id),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A failure ends a pending order only: it can never undo a paid one (M9).</summary>
    private Task<int> CloseAsync(Guid orderId, string status, short code, string? paymentId, DateTime now, CancellationToken cancellationToken) =>
        db.OnlinePayments
            .Where(p => p.OrderId == orderId && p.Status == OnlinePaymentStatus.Pending)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(p => p.Status, status)
                    .SetProperty(p => p.PayHereStatusCode, code)
                    .SetProperty(p => p.PayHerePaymentId, paymentId)
                    .SetProperty(p => p.NotifiedAt, now),
                cancellationToken);

    /// <summary>Logged, because the answer says nothing: an unknown order and a forged one look alike from outside.</summary>
    private bool Refuse(string why, string? orderId)
    {
        LogRefused(logger, why, orderId);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PayHere notification refused ({Why}) for order {OrderId}")]
    private static partial void LogRefused(ILogger logger, string why, string? orderId);

    private static SealedSecret Sealed(PaymentMerchant merchant) => new(merchant.SecretKeyId, merchant.SecretNonce, merchant.SecretCipher);

    /// <summary>PayHere asks for two names; a person with one name gives it for both.</summary>
    private static (string First, string Last) SplitName(string fullName)
    {
        var name = fullName.Trim();
        var space = name.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (name, name) : (name[..space], name[(space + 1)..].Trim());
    }

    private static bool IsEmail(string? value) =>
        value is { Length: > 0 and <= MaxEmail } && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
}
