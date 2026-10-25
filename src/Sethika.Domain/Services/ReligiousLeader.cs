namespace Sethika.Domain.Services;

/// <summary>
/// A religious leader on an institution's roster (Phase E, V1). Maps <c>org.ReligiousLeaders</c>.
/// </summary>
/// <remarks>
/// A person record, not an account: many leaders never sign in, and an administrator answers for
/// them. A leader who does sign in is linked by <see cref="UserId"/> and holds the LEADER role.
/// </remarks>
public class ReligiousLeader
{
    public int Id { get; set; }

    /// <summary>The leader's address in APIs (v7, set in C#).</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }

    /// <summary>Free text: Ven., Rev., Fr., Moulavi, Kurukkal ...</summary>
    public string? Title { get; set; }

    /// <summary>Masked in the database. Without one the leader gets no texts (V14).</summary>
    public string? Phone { get; set; }

    /// <summary>The language of texts to a leader without an account: en, si or ta (V14).</summary>
    public string Language { get; set; } = "si";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>When a leader is normally free. Maps <c>org.LeaderWeeklyHours</c>; a filter, not a guarantee (V2).</summary>
public class LeaderWeeklyHours
{
    public int Id { get; set; }
    public int LeaderId { get; set; }
    public int InstitutionId { get; set; }

    /// <summary>0 Sunday ... 6 Saturday, as <see cref="System.DayOfWeek"/>.</summary>
    public byte DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// Whether the Colombo-time interval <c>[start, end)</c> falls inside these hours. A service that
    /// runs past midnight is never inside one day's hours.
    /// </summary>
    public bool Covers(DateTime colomboStart, DateTime colomboEnd) =>
        (byte)colomboStart.DayOfWeek == DayOfWeek
        && colomboEnd.Date == colomboStart.Date
        && TimeOnly.FromDateTime(colomboStart) >= StartTime
        && TimeOnly.FromDateTime(colomboEnd) <= EndTime;
}

/// <summary>A leader's leave. Maps <c>org.LeaderTimeOff</c>; writes TimeOff blocks (V2).</summary>
public class LeaderTimeOff
{
    public int Id { get; set; }
    public int LeaderId { get; set; }
    public int InstitutionId { get; set; }

    /// <summary>UTC, on the grid.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>UTC, on the grid.</summary>
    public DateTime EndsAt { get; set; }

    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>One 15-minute block of a leader's committed time. Maps <c>booking.LeaderTimeBlocks</c>.</summary>
public class LeaderTimeBlock
{
    public int LeaderId { get; set; }

    /// <summary>UTC, on the grid. With <see cref="LeaderId"/>, the primary key: the overlap guarantee.</summary>
    public DateTime BlockStart { get; set; }

    public int InstitutionId { get; set; }

    /// <summary>A <see cref="LeaderTimeBlockKind"/>.</summary>
    public string Kind { get; set; } = null!;

    public long? AppointmentId { get; set; }
    public int? TimeOffId { get; set; }
}

/// <summary><c>booking.LeaderTimeBlocks.Kind</c>.</summary>
public static class LeaderTimeBlockKind
{
    public const string Appointment = "Appointment";
    public const string TimeOff = "TimeOff";
}
