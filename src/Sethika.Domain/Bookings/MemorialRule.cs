namespace Sethika.Domain.Bookings;

/// <summary>
/// One observance a faith keeps after a death, such as the 7th day or the yearly almsgiving (plan 07,
/// H6). Maps <c>core.MemorialRules</c>, reference data seeded as drafts (H7).
/// </summary>
/// <remarks>
/// Configuration, not code: a faith's customs are rows, so a corrected offset or a new observance
/// changes the dates the planner shows without a release. Read-only to the application.
/// </remarks>
public class MemorialRule
{
    public short Id { get; set; }
    public int ReligionLookupId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }

    /// <summary>Days after the date of passing, added after <see cref="OffsetMonths"/>.</summary>
    public short OffsetDays { get; set; }

    /// <summary>Months after the date of passing, added first.</summary>
    public short OffsetMonths { get; set; }

    /// <summary>Kept every year on the anniversary of its first date, as a yearly almsgiving is.</summary>
    public bool RepeatsAnnually { get; set; }

    public short SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
