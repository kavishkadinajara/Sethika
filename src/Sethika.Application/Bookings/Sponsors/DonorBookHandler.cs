using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>
/// The institution's donor book (plan 08, K1): staff add the people who give regularly, many of whom
/// never use the app, and change their details.
/// </summary>
/// <remarks>
/// Every method acts for <c>institutionId</c>, the institution in the caller's token. The donor book is
/// a tenant table under Row-Level Security (<c>sec.fn_Tenant</c>), and a lookup still names the
/// institution, so another institution's donor reads as not found.
///
/// A person whose phone is a Sethika account's confirmed phone is linked to that account when added or
/// changed, so they will see their standing days in My bookings. An unconfirmed phone proves nothing
/// about who holds it, so it links nothing; nor does a deleted account, whose phone a new one may hold.
/// A change of link carries the standing days still to come with it (K37). The person's own sign-up
/// or sign-in links them too (<see cref="IDonorBookLinks"/>, K36).
/// </remarks>
public sealed class DonorBookHandler(ISethikaDbContext db, IPhoneNumberNormalizer phoneNumbers, TimeProvider timeProvider)
{
    private const string PhoneIndex = "UQ_InstDonor_Phone";
    private const string AccountIndex = "UX_InstDonor_User";
    private const int MaxName = 150;
    private const int MaxPhone = 20;
    private const int MaxNotes = 500;

    private static readonly string[] Languages = ["en", "si", "ta"];

    /// <summary>A new, active person in the donor book.</summary>
    public async Task<DonorChangeResult> CreateAsync(int institutionId, Guid userId, DonorInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var donor = new InstitutionDonor
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            CreatedAt = now,
            CreatedBy = userId,
        };

        var invalid = await ApplyAsync(input, donor, [], userId, now, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
        {
            return invalid;
        }

        db.InstitutionDonors.Add(donor);
        return await SaveAsync(donor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the donor's details, if it is still the <see cref="DonorInput.Version"/> the editor read.</summary>
    public async Task<DonorChangeResult> UpdateAsync(
        int institutionId,
        Guid userId,
        Guid publicId,
        DonorInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var donor = await db.InstitutionDonors
            .SingleOrDefaultAsync(d => d.PublicId == publicId && d.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (donor is null)
        {
            return DonorChangeResult.Failed(DonorChangeResult.DonorNotFound);
        }

        var fields = new List<string>();
        if (input.Version is not { Length: 8 })
        {
            fields.Add("version");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var linkedBefore = donor.UserId;
        var invalid = await ApplyAsync(input, donor, fields, userId, now, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
        {
            return invalid;
        }

        db.Entry(donor).Property(d => d.RowVersion).OriginalValue = input.Version!;
        donor.UpdatedAt = now;
        donor.UpdatedBy = userId;

        if (donor.UserId == linkedBefore)
        {
            return await SaveAsync(donor, cancellationToken).ConfigureAwait(false);
        }

        // The link changed (K37): the standing days still to come follow it in the same transaction, so
        // the account now linked sees them in My bookings and the one unlinked no longer does.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var saved = await SaveAsync(donor, cancellationToken).ConfigureAwait(false);
        if (!saved.Succeeded)
        {
            return saved;
        }

        await db.Bookings
            .Where(b => b.InstitutionDonorId == donor.Id && b.InstitutionId == institutionId && b.Source == Booking.StandingSource
                        && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved))
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.DonorUserId, donor.UserId), cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return saved;
    }

    private async Task<DonorChangeResult> SaveAsync(InstitutionDonor donor, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == PhoneIndex)
        {
            return DonorChangeResult.Failed(DonorChangeResult.PhoneTaken);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == AccountIndex)
        {
            return DonorChangeResult.Failed(DonorChangeResult.AccountTaken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DonorChangeResult.Failed(DonorChangeResult.Stale);
        }

        return DonorChangeResult.For(donor.PublicId, donor.RowVersion);
    }

    /// <summary>
    /// Checks the form and copies it onto <paramref name="donor"/>: the refusal, or
    /// <see langword="null"/> when it is copied.
    /// </summary>
    /// <param name="invalid">Fields already found wrong by the caller; nothing is copied if any are.</param>
    private async Task<DonorChangeResult?> ApplyAsync(
        DonorInput input,
        InstitutionDonor donor,
        List<string> invalid,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var name = Tidy(input.FullName);
        if (name is null || name.Length > MaxName)
        {
            invalid.Add("fullName");
        }

        // A phone that cannot be texted is refused here rather than after paying to send to it.
        string? phone = null;
        if (phoneNumbers.TryNormalize(Tidy(input.Phone), out var normalized) && normalized.Length <= MaxPhone)
        {
            phone = normalized;
        }
        else
        {
            invalid.Add("phone");
        }

        var notes = Tidy(input.Notes);
        if (notes is { Length: > MaxNotes })
        {
            invalid.Add("notes");
        }

        var language = Tidy(input.Language) ?? "si";
        if (!Languages.Contains(language, StringComparer.Ordinal))
        {
            invalid.Add("language");
        }

        if (invalid.Count > 0)
        {
            return DonorChangeResult.Invalid(invalid);
        }

        donor.UserId = await db.Users
            .Where(u => u.PhoneNumber == phone && u.PhoneConfirmed && !u.IsDeleted)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        donor.FullName = name!;
        donor.Phone = phone!;
        donor.Notes = notes;
        donor.Language = language;
        donor.SetTextConsent(input.TextConsent == true, userId, now);
        return null;
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
