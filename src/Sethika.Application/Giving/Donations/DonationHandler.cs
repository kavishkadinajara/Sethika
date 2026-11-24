using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Needs;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Donations;

/// <summary>
/// Recording a gift at the counter and voiding one (plan 09, M1-M6).
/// </summary>
/// <remarks>
/// <para>
/// Every method acts for <c>institutionId</c>, the institution in the caller's token. Donations are a
/// tenant table under Row-Level Security, and every lookup names the institution as well, so another
/// institution's receipt reads as not found.
/// </para>
/// <para>
/// Recording is one transaction (M1): the receipt number from the institution's own series (M2, given
/// back if anything after it fails), the row, the audit row, and for a donor book row the outbox
/// message that may text the receipt (M5). A walk-in's typed phone is never texted, so a walk-in's gift
/// writes no message at all.
/// </para>
/// <para>
/// A gift is never changed. Voiding (M6) is a second person's act: the recorder is refused here first,
/// with its own code, and by <c>CK_Donations_VoidFourEyes</c> if this check were ever wrong. The
/// permission behind it needs a step-up, which the permission check asks for before this runs.
/// </para>
/// </remarks>
public sealed class DonationHandler(ISethikaDbContext db, INumberSeries numbers, IPhoneNumberNormalizer phoneNumbers, TimeProvider timeProvider)
{
    /// <summary>What staff may choose at the counter (M1). <c>PAYHERE</c> is set by the gateway only (M9).</summary>
    public static readonly IReadOnlyList<string> CounterMethods = ["CASH", "BANK_TRANSFER", "CHEQUE", "CARD"];

    /// <summary>An outbox message's aggregate, and the audit rows' entity.</summary>
    public const string Aggregate = "Donation";

    /// <summary>Within <c>DECIMAL(12,2)</c>, and far above any gift at a counter.</summary>
    public const decimal MaxAmount = 100_000_000m;

    /// <summary>Within <c>DECIMAL(10,2)</c>.</summary>
    public const decimal MaxQuantity = 1_000_000m;

    private const int MaxName = 150;
    private const int MaxPhone = 20;
    private const int MaxItems = 500;

    /// <summary>A past day's gift is stored at noon in Colombo, so it falls on that day in every zone's report (M23).</summary>
    private static readonly TimeOnly PastDayTime = new(12, 0);

    /// <summary>Records a gift and gives it the institution's next receipt number.</summary>
    public async Task<DonationResult> RecordAsync(int institutionId, Guid userId, DonationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = SriLankaTime.Today(now);
        var invalid = new List<string>();

        var kind = input.Kind is Donation.Money or Donation.InKind ? input.Kind : null;

        if (kind is null)
        {
            invalid.Add("kind");
        }

        int? methodId = null;
        string? items = null;
        if (kind == Donation.Money)
        {
            if (!IsAmount(input.Amount, MaxAmount))
            {
                invalid.Add("amount");
            }

            methodId = CounterMethods.Contains(input.Method ?? string.Empty, StringComparer.Ordinal)
                ? await MethodIdAsync(input.Method!, cancellationToken).ConfigureAwait(false)
                : null;

            if (methodId is null)
            {
                invalid.Add("method");
            }

            // Items, a count or a worth belong to a gift of items; sent with money, the form is wrong.
            if (input.Items is not null || input.Quantity is not null || input.EstimatedValue is not null)
            {
                invalid.Add("items");
            }
        }
        else if (kind == Donation.InKind)
        {
            items = Tidy(input.Items);
            if (items is null || items.Length > MaxItems)
            {
                invalid.Add("items");
            }

            // A gift of items against a need must say how many: that is what the need counts (M15).
            if (input.Quantity is null ? input.Need is not null : !IsAmount(input.Quantity, MaxQuantity))
            {
                invalid.Add("quantity");
            }

            if (input.EstimatedValue is not null && !IsAmount(input.EstimatedValue, MaxAmount))
            {
                invalid.Add("estimatedValue");
            }

            if (input.Amount is not null)
            {
                invalid.Add("amount");
            }

            if (input.Method is not null)
            {
                invalid.Add("method");
            }
        }

        if (input.Date is not { } date || date > today || date < today.AddYears(-1))
        {
            invalid.Add("date");
        }

        var note = Tidy(input.Note);
        if (note is { Length: > Donation.MaxReason })
        {
            invalid.Add("note");
        }

        var walkInName = Tidy(input.DonorName);
        var walkInPhoneTyped = Tidy(input.DonorPhone);
        string? walkInPhone = null;
        if (input.Donor is not null)
        {
            // A donor book row brings its own name and phone (M3); a typed one beside it is a form error.
            if (walkInName is not null || walkInPhoneTyped is not null)
            {
                invalid.Add("donor");
            }
        }
        else
        {
            if (walkInName is null || walkInName.Length > MaxName)
            {
                invalid.Add("donorName");
            }

            if (walkInPhoneTyped is not null)
            {
                if (phoneNumbers.TryNormalize(walkInPhoneTyped, out var normalized) && normalized.Length <= MaxPhone)
                {
                    walkInPhone = normalized;
                }
                else
                {
                    invalid.Add("donorPhone");
                }
            }
        }

        if (invalid.Count > 0)
        {
            return DonationResult.Invalid(invalid);
        }

        var donation = new Donation
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            Kind = kind!,
            Amount = kind == Donation.Money ? input.Amount : null,
            PaymentMethodLookupId = methodId,
            ItemDescription = items,
            Quantity = kind == Donation.InKind ? input.Quantity : null,
            EstimatedValue = kind == Donation.InKind ? input.EstimatedValue : null,
            Note = note,
            DonatedAt = input.Date == today ? now : SriLankaTime.ToUtc(input.Date!.Value, PastDayTime),
            RecordedBy = userId,
            RecordedAt = now,
        };

        if (input.Donor is { } donorId)
        {
            var donor = await db.InstitutionDonors
                .AsNoTracking()
                .Where(d => d.PublicId == donorId && d.InstitutionId == institutionId && d.IsActive)
                .Select(d => new { d.Id, d.FullName, d.Phone, d.UserId })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (donor is null)
            {
                return DonationResult.Failed(DonationResult.DonorNotFound);
            }

            donation.InstitutionDonorId = donor.Id;
            donation.DonorName = donor.FullName;
            donation.DonorPhone = donor.Phone;
            donation.DonorUserId = donor.UserId;
        }
        else
        {
            donation.DonorName = walkInName!;
            donation.DonorPhone = walkInPhone;
        }

        if (input.Need is { } needPublicId)
        {
            donation.NeedId = await db.Needs
                .Where(n => n.PublicId == needPublicId && n.InstitutionId == institutionId)
                .Select(n => (int?)n.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (donation.NeedId is null)
            {
                return DonationResult.Failed(DonationResult.NeedNotFound);
            }
        }

        var scope = await db.Institutions
            .Where(i => i.Id == institutionId)
            .Select(i => i.InstitutionCode)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        donation.ReceiptNo = await numbers
            .NextAsync(NumberSeriesDefinition.Receipt, scope, today.Year, cancellationToken)
            .ConfigureAwait(false);

        // Refused here, the transaction's end gives the receipt number back (M2).
        if (donation.NeedId is { } needId && await CountTowardNeedAsync(needId, donation, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return DonationResult.Failed(refused);
        }

        db.Donations.Add(donation);
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = userId,
            InstitutionId = institutionId,
            Action = "Record",
            EntityName = Aggregate,
            EntityId = donation.ReceiptNo,
            // Codes and figures only: no names or phones (the receipt holds those).
            NewValues = JsonSerializer.Serialize(new
            {
                publicId = donation.PublicId,
                kind = donation.Kind,
                amount = donation.Amount,
                method = kind == Donation.Money ? input.Method : null,
                quantity = donation.Quantity,
                donorBook = donation.InstitutionDonorId is not null,
            }),
            OccurredAt = now,
        });

        if (donation.InstitutionDonorId is not null)
        {
            db.OutboxMessages.Add(DonationTexts.Message(donation.PublicId, now));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return DonationResult.For(donation.PublicId, donation.ReceiptNo, donation.RowVersion);
    }

    /// <summary>Voids the gift numbered <paramref name="receiptNo"/>, if it is still the version the list read.</summary>
    public async Task<DonationResult> VoidAsync(int institutionId, Guid userId, string receiptNo, VoidInput input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptNo);
        ArgumentNullException.ThrowIfNull(input);

        var donation = await db.Donations
            .SingleOrDefaultAsync(d => d.ReceiptNo == receiptNo && d.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (donation is null)
        {
            return DonationResult.Failed(DonationResult.NotFound);
        }

        if (input.Version is not { Length: 8 })
        {
            return DonationResult.Invalid(["version"]);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refused = donation.Void(userId, input.Reason, now);
        if (refused == DonationErrors.InvalidReason)
        {
            return DonationResult.Invalid(["reason"]);
        }

        if (refused is not null)
        {
            return DonationResult.Failed(refused);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Leaving every total includes the need's (M30). A stale void below rolls this back.
        if (donation is { Kind: Donation.InKind, NeedId: { } needId, Quantity: { } quantity })
        {
            await db.TakeBackArrivalAsync(needId, quantity, cancellationToken).ConfigureAwait(false);
        }

        db.Entry(donation).Property(d => d.RowVersion).OriginalValue = input.Version;
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = userId,
            InstitutionId = institutionId,
            Action = "Void",
            EntityName = Aggregate,
            EntityId = donation.ReceiptNo,
            OldValues = JsonSerializer.Serialize(new { publicId = donation.PublicId, isVoided = false }),
            NewValues = JsonSerializer.Serialize(new { publicId = donation.PublicId, isVoided = true }),
            OccurredAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DonationResult.Failed(DonationResult.Stale);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return DonationResult.For(donation.PublicId, donation.ReceiptNo, donation.RowVersion);
    }

    /// <summary>
    /// Items count toward the need (M15, M29); money only names it, so the need must simply still be open.
    /// </summary>
    /// <returns>The refusal, or <see langword="null"/> when the gift may stand.</returns>
    private async Task<string?> CountTowardNeedAsync(int needId, Donation donation, CancellationToken cancellationToken)
    {
        if (donation.Kind == Donation.InKind)
        {
            return await db.CountArrivalAsync(needId, donation.Quantity!.Value, donation.RecordedAt, cancellationToken).ConfigureAwait(false) == 1
                ? null
                : await db.WhyNotAsync(needId, cancellationToken).ConfigureAwait(false);
        }

        var open = await db.Needs.AnyAsync(n => n.Id == needId && n.Status == NeedStatus.Open, cancellationToken).ConfigureAwait(false);
        return open ? null : NeedErrors.Closed;
    }

    private Task<int?> MethodIdAsync(string code, CancellationToken cancellationToken) =>
        db.LookupValues
            .Where(v => v.Code == code && v.IsActive)
            .Join(db.LookupTypes.Where(t => t.Code == LookupValue.PaymentMethod), v => v.LookupTypeId, t => t.Id, (v, t) => (int?)v.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>More than 0, at most <paramref name="max"/>, and in whole cents.</summary>
    private static bool IsAmount(decimal? value, decimal max) =>
        value is { } v && v > 0 && v <= max && decimal.Round(v, 2) == v;

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
