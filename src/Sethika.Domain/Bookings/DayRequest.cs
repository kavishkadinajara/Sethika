namespace Sethika.Domain.Bookings;

/// <summary>
/// A donor asks for a day a standing sponsor holds (plan 08, K6). Maps <c>booking.DayRequests</c>.
/// </summary>
/// <remarks>
/// Asking holds nothing: the day stays the sponsor's until the institution gives it. Giving it is one
/// transaction elsewhere - the standing day released, its booking cancelled, the asker booked - and
/// ends here with <see cref="Give"/>. One open request per donor and slot-day
/// (<c>UX_DayRequests_OpenPerDonor</c>).
/// </remarks>
public class DayRequest
{
    public int Id { get; set; }

    /// <summary>The request's address: <c>DR-2026-000123</c>, from <c>core.NumberSeries</c>.</summary>
    public string RequestNo { get; set; } = null!;

    public int InstitutionId { get; set; }
    public int MealSlotId { get; set; }
    public DateOnly RequestedDate { get; set; }
    public Guid DonorUserId { get; set; }
    public short Headcount { get; set; }

    /// <summary><see cref="Bookings.FulfilmentMode"/>: how the asker gives the meal if the day is theirs.</summary>
    public string FulfilmentMode { get; set; } = Bookings.FulfilmentMode.DonorBrings;

    public string? Note { get; set; }

    /// <summary>One of <see cref="Open"/>, <see cref="Given"/>, <see cref="Declined"/>, <see cref="Withdrawn"/>.</summary>
    public string Status { get; private set; } = Open;

    /// <summary>Given: the sponsorship whose day was let go.</summary>
    public int? StandingSponsorshipId { get; private set; }

    /// <summary>Given: the asker's booking.</summary>
    public long? BookingId { get; private set; }

    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DeclineReason { get; private set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public const string Open = "Open";
    public const string Given = "Given";
    public const string Declined = "Declined";
    public const string Withdrawn = "Withdrawn";

    /// <summary>The longest note or decline reason: both are <c>NVARCHAR(300)</c>.</summary>
    public const int MaxText = 300;

    public bool IsOpen => Status == Open;

    /// <summary>Open → Given: <paramref name="sponsorshipId"/>'s day was let go and <paramref name="bookingId"/> made.</summary>
    /// <returns>Whether it moved; an answered request stays as it was answered.</returns>
    public bool Give(int sponsorshipId, long bookingId, Guid userId, DateTime now)
    {
        if (!IsOpen)
        {
            return false;
        }

        Status = Given;
        StandingSponsorshipId = sponsorshipId;
        BookingId = bookingId;
        DecidedBy = userId;
        DecidedAt = now;
        return true;
    }

    /// <summary>Open → Declined, with an optional reason the asker reads when they sign in.</summary>
    /// <returns>Whether it moved; an answered request stays as it was answered.</returns>
    public bool Decline(string? reason, Guid userId, DateTime now)
    {
        if (!IsOpen)
        {
            return false;
        }

        Status = Declined;
        DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DecidedBy = userId;
        DecidedAt = now;
        return true;
    }
}
