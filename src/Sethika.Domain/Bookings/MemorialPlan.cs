namespace Sethika.Domain.Bookings;

/// <summary>
/// A donor's Memorial Dana Planner entry: someone who died, and when (plan 07, H6). Maps
/// <c>booking.MemorialPlans</c>.
/// </summary>
/// <remarks>
/// The plan stores no dates of its own: each observance is worked out from <see cref="DateOfPassing"/>
/// and the religion's <see cref="MemorialRule"/>s by <see cref="MemorialObservances"/>, so a corrected
/// rule moves every plan's dates with it. Row-Level Security shows a plan to its donor alone, never to
/// an institution (H13).
/// </remarks>
public class MemorialPlan
{
    /// <summary>The longest name of the person who died: <c>DeceasedName</c> is <c>NVARCHAR(150)</c>.</summary>
    public const int MaxDeceasedName = 150;

    /// <summary>The longest note: <c>Notes</c> is <c>NVARCHAR(500)</c>.</summary>
    public const int MaxNotes = 500;

    public int Id { get; set; }

    /// <summary>How the donor's screens and the API name it; <see cref="Id"/> never leaves the server.</summary>
    public Guid PublicId { get; set; }

    public Guid DonorUserId { get; set; }
    public string DeceasedName { get; set; } = string.Empty;
    public int? RelationshipLookupId { get; set; }
    public DateOnly DateOfPassing { get; set; }
    public int ReligionLookupId { get; set; }
    public long? PhotoFileId { get; set; }
    public string? Notes { get; set; }

    /// <summary>False once the donor archives it: no more reminders, and the bookings made from it stay.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
