using System.Globalization;
using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Domain.Core;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Donations;

/// <summary>
/// The receipt text (plan 09, M5), written to the outbox in the transaction that records the gift and
/// sent by <see cref="DonationSmsHandler"/>.
/// </summary>
public static class DonationTexts
{
    /// <summary>A gift was recorded against a donor book row (or, from D.3, given online).</summary>
    public const string Event = "DonationRecorded";

    /// <summary>Money: names the amount.</summary>
    public const string Receipt = "DONATION_RECEIPT";

    /// <summary>Items: names none of them, because staff type them and a text never carries typed words.</summary>
    public const string ReceiptItems = "DONATION_RECEIPT_ITEMS";

    /// <summary>The message for the gift <paramref name="publicId"/>.</summary>
    public static OutboxMessage Message(Guid publicId, DateTime now) =>
        new()
        {
            EventType = Event,
            AggregateType = DonationHandler.Aggregate,
            AggregateId = publicId.ToString(),
            Payload = JsonSerializer.Serialize(new { donation = publicId }),
            OccurredAt = now,
        };

    /// <summary>The figure as the receipt shows it: <c>5,000.00</c>.</summary>
    public static string Amount(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>One receipt is texted once, whatever retries the outbox makes.</summary>
    public static string DedupKey(int institutionId, string receiptNo) => $"receipt:{institutionId}:{receiptNo}";
}

/// <summary>
/// Sends the receipt text. A donor with an account is texted as any account is, in their language; a
/// donor book row without one is texted at its phone only if staff ticked its consent (plan 08, Q2), and
/// otherwise not at all. A gift voided before the text went is not texted (M5: a void texts nothing).
/// </summary>
public sealed class DonationSmsHandler(IGivingQueries queries, IUserTextMessages texts) : IOutboxHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [DonationTexts.Event];

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { Donation: var publicId } || publicId == Guid.Empty)
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) does not name a donation.");
        }

        // A donation is never deleted, so a missing one is an error to retry and log.
        var gift = await queries.GetReceiptTextAsync(publicId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Outbox message {message.Id} names donation {publicId}, which does not exist.");

        if (gift.IsVoided)
        {
            return;
        }

        var money = gift.Kind == Donation.Money;
        var template = money ? DonationTexts.Receipt : DonationTexts.ReceiptItems;
        var dedupKey = DonationTexts.DedupKey(gift.InstitutionId, gift.ReceiptNo);

        if (gift.DonorUserId is { } account)
        {
            await texts.SendAsync(account, template, Values(gift, money, gift.Language), dedupKey, cancellationToken).ConfigureAwait(false);
        }
        else if (gift is { TextConsent: true, DonorBookPhone: { } phone })
        {
            var language = gift.Language ?? "si";
            await texts.SendToPhoneAsync(phone, language, template, Values(gift, money, language), dedupKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> Values(DonationTextDetails gift, bool money, string? language)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["receiptNo"] = gift.ReceiptNo,
            ["institution"] = InstitutionName(language, gift),
        };

        if (money && gift.Amount is { } amount)
        {
            values["amount"] = DonationTexts.Amount(amount);
        }

        return values;
    }

    private static string InstitutionName(string? language, DonationTextDetails gift) =>
        (language switch
        {
            "si" => gift.InstitutionNameSi,
            "ta" => gift.InstitutionNameTa,
            _ => null,
        }) is { Length: > 0 } local
            ? local
            : gift.InstitutionName;

    private sealed record Payload(Guid Donation);
}
