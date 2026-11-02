using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Sharing;

/// <summary>An invitation to share a booking, as the donor's form sends it.</summary>
public sealed record InviteContributorCommand(string? Name, string? Note);

/// <summary>The outcome of a donor's change to their invitations: the new one and its link's secret, or why not.</summary>
/// <param name="Token">The link's secret (H16), returned once and never stored.</param>
public sealed record ContributorResult(string? Error, IReadOnlyList<string> Fields, Guid? Contributor, string? Token)
{
    /// <summary>Some fields are missing or wrong; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No booking of this donor's with that number.</summary>
    public const string BookingNotFound = "booking_not_found";

    /// <summary>The booking is cancelled, rejected or past: there is nothing left to share.</summary>
    public const string BookingClosed = "booking_closed";

    /// <summary>The booking already holds <see cref="BookingContributor.MaxPerBooking"/> invitations.</summary>
    public const string TooManyInvitations = "too_many_invitations";

    /// <summary>No invitation with that id on this donor's booking.</summary>
    public const string InvitationNotFound = "invitation_not_found";

    /// <summary>The invitation was joined or declined, so it can no longer be withdrawn.</summary>
    public const string InvitationAnswered = "invitation_answered";

    public bool Succeeded => Error is null;

    internal static ContributorResult Ok(Guid contributor, string? token) => new(null, [], contributor, token);

    internal static ContributorResult Failed(string error, params string[] fields) => new(error, fields, null, null);
}

/// <summary>The donor's invitations to share a booking (plan 07, H16, H17).</summary>
/// <remarks>
/// The donor sends the link themselves; Sethika texts nobody. Row-Level Security already limits the
/// donor to their own bookings and invitations; the donor id is checked as well, as with stopping a
/// yearly booking.
/// </remarks>
public sealed class ContributorHandler(ISethikaDbContext db, IBookingRowLocks locks, TimeProvider timeProvider)
{
    public async Task<ContributorResult> InviteAsync(Guid donorId, string bookingNo, InviteContributorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = Tidy(command.Name);
        var note = Tidy(command.Note);
        var invalid = new List<string>();
        if (name is null or { Length: > BookingContributor.MaxName })
        {
            invalid.Add("name");
        }

        if (note is { Length: > BookingContributor.MaxNote })
        {
            invalid.Add("note");
        }

        if (invalid.Count > 0)
        {
            return ContributorResult.Failed(ContributorResult.InvalidDetails, [.. invalid]);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var booking = await (
                from b in db.Bookings
                join s in db.SlotInstances on b.SlotInstanceId equals s.Id
                where b.BookingNo == bookingNo && b.DonorUserId == donorId
                select new { b.Id, b.InstitutionId, b.Status, s.SlotDate })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (booking is null)
        {
            return ContributorResult.Failed(ContributorResult.BookingNotFound);
        }

        if (!BookingContributor.IsShareable(booking.Status, booking.SlotDate, SriLankaTime.Today(now)))
        {
            return ContributorResult.Failed(ContributorResult.BookingClosed);
        }

        // The booking's row lock queues a second invitation made at the same moment behind this one,
        // so the count it reads includes this one's: the limit holds under a double click.
        await locks.LockAsync(booking.Id, cancellationToken).ConfigureAwait(false);

        if (await db.BookingContributors.CountAsync(c => c.BookingId == booking.Id, cancellationToken).ConfigureAwait(false)
            >= BookingContributor.MaxPerBooking)
        {
            return ContributorResult.Failed(ContributorResult.TooManyInvitations);
        }

        var (token, hash) = ShareToken.New();
        var contributor = new BookingContributor
        {
            PublicId = Guid.CreateVersion7(now),
            BookingId = booking.Id,
            InstitutionId = booking.InstitutionId,
            Name = name!,
            ContributionNote = note,
            InviteTokenHash = hash,
            Status = ContributorStatus.Invited,
            InvitedBy = donorId,
            InvitedAt = now,
        };

        db.BookingContributors.Add(contributor);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ContributorResult.Ok(contributor.PublicId, token);
    }

    /// <summary>
    /// Withdraws invitation <paramref name="contributorId"/> on the donor's booking while nobody has
    /// answered it (H17): its link then opens nothing. <see langword="null"/> on success, else
    /// <see cref="ContributorResult.InvitationNotFound"/> or <see cref="ContributorResult.InvitationAnswered"/>.
    /// </summary>
    public async Task<string?> WithdrawAsync(Guid donorId, string bookingNo, Guid contributorId, CancellationToken cancellationToken = default)
    {
        var invitation = db.BookingContributors.Where(c =>
            c.PublicId == contributorId
            && c.InvitedBy == donorId
            && db.Bookings.Any(b => b.Id == c.BookingId && b.BookingNo == bookingNo && b.DonorUserId == donorId));

        var withdrawn = await invitation
            .Where(c => c.Status == ContributorStatus.Invited)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (withdrawn == 1)
        {
            return null;
        }

        return await invitation.AnyAsync(cancellationToken).ConfigureAwait(false)
            ? ContributorResult.InvitationAnswered
            : ContributorResult.InvitationNotFound;
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
