namespace Sethika.Domain.Bookings;

/// <summary>
/// Someone a donor invited to share a booking, a co-sponsor (plan 07, H16-H19). Maps
/// <c>booking.BookingContributors</c>.
/// </summary>
/// <remarks>
/// The donor makes a link and sends it themselves; whoever opens it, signed in, joins or declines.
/// Only the hash of the link's secret is stored (<see cref="ShareToken"/>). Row-Level Security shows
/// the row to the institution, the person who answered and the donor who invited (H18).
/// </remarks>
public class BookingContributor
{
    /// <summary>The longest name: <c>Name</c> is <c>NVARCHAR(150)</c>.</summary>
    public const int MaxName = 150;

    /// <summary>The longest note: <c>ContributionNote</c> is <c>NVARCHAR(200)</c>.</summary>
    public const int MaxNote = 200;

    /// <summary>The most invitations one booking holds, answered or not (H16).</summary>
    public const int MaxPerBooking = 10;

    public long Id { get; set; }

    /// <summary>How the donor's screens and the API name it; <see cref="Id"/> never leaves the server.</summary>
    public Guid PublicId { get; set; }

    public long BookingId { get; set; }
    public int InstitutionId { get; set; }

    /// <summary>Who joined or declined; <see langword="null"/> while the invitation waits.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The name the donor gave the person they invited.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>"Brings dessert": what the donor wrote for them.</summary>
    public string? ContributionNote { get; set; }

    /// <summary><see cref="ShareToken.HashOf"/> of the link's secret.</summary>
    public string InviteTokenHash { get; set; } = string.Empty;

    /// <summary>A <see cref="ContributorStatus"/> value.</summary>
    public string Status { get; set; } = ContributorStatus.Invited;

    public DateTime? JoinedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }

    /// <summary>The booking's donor, who made the link.</summary>
    public Guid InvitedBy { get; set; }

    public DateTime InvitedAt { get; set; }

    /// <summary>
    /// Whether a booking in <paramref name="status"/> on <paramref name="date"/> can still be shared
    /// on <paramref name="today"/> (H17): a live booking, today or later. A cancelled, rejected or past
    /// one shows its state instead.
    /// </summary>
    public static bool IsShareable(string status, DateOnly date, DateOnly today) =>
        status is BookingStatus.Requested or BookingStatus.Approved && date >= today;
}

/// <summary>The values of <c>booking.BookingContributors.Status</c> (<c>CK_Contributor_Status</c>).</summary>
public static class ContributorStatus
{
    public const string Invited = "Invited";
    public const string Joined = "Joined";
    public const string Declined = "Declined";
}
