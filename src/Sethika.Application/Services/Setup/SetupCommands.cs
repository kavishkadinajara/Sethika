namespace Sethika.Application.Services.Setup;

/// <summary>A religious service as the institution's service form sends it.</summary>
/// <param name="DurationMinutes">15 to 720, a whole number of 15-minute blocks.</param>
/// <param name="BufferMinutes">Travel time either side of an at-home service (V5): 0 to 180, in 15s.</param>
/// <param name="LocationMode"><c>AtInstitution</c>, <c>AtDonorPlace</c> or <c>Either</c>.</param>
/// <param name="Version">An edit's: the type's <c>RowVersion</c> as the editor read it. A new type has none.</param>
public sealed record ServiceTypeInput(
    string? Name,
    string? NameSi,
    string? NameTa,
    string? Description,
    short? DurationMinutes,
    short? BufferMinutes,
    byte? LeadersRequired,
    string? LocationMode,
    short? MinAdvanceDays,
    short? MaxAdvanceDays,
    byte[]? Version = null);

/// <summary>A religious leader as the roster form sends it.</summary>
/// <param name="Phone">As typed; texts go here (V14). Required to link an account.</param>
/// <param name="Language">Of texts to a leader without an account: <c>en</c>, <c>si</c> (the default) or <c>ta</c>.</param>
/// <param name="LinkAccount">
/// Whether the leader signs in: links the account with <paramref name="Phone"/> (§6). <see langword="false"/> unlinks.
/// </param>
/// <param name="Version">An edit's: the leader's <c>RowVersion</c> as the editor read it.</param>
public sealed record LeaderInput(
    string? DisplayName,
    string? NameSi,
    string? NameTa,
    string? Title,
    string? Phone,
    string? Language,
    bool? LinkAccount,
    byte[]? Version = null);

/// <summary>One span of a leader's usual week.</summary>
/// <param name="DayOfWeek">0 Sunday ... 6 Saturday.</param>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
/// <param name="EndTime"><c>HH:mm</c>, after <paramref name="StartTime"/>, the same day.</param>
public sealed record WeeklyHoursInput(byte? DayOfWeek, string? StartTime, string? EndTime);

/// <summary>A leader's leave.</summary>
/// <param name="StartsAt">With its offset (the screens send +05:30); on the 15-minute grid.</param>
/// <param name="EndsAt">After <paramref name="StartsAt"/>, on the grid, and not yet past.</param>
public sealed record TimeOffInput(DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Reason);

/// <summary>The outcome of a change to the institution's services, leaders, hours or leave.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="PublicId">The service type or leader created or changed.</param>
/// <param name="Version">Its <c>RowVersion</c> after the change, for the next edit.</param>
/// <param name="TimeOffId">The leave added.</param>
/// <param name="Appointments">
/// Appointment numbers the change touches: for <c>time_off_clashes</c> the ones in the way, for a
/// leader taken off the roster the ones they are still assigned to, which the change does not undo.
/// </param>
public sealed record SetupChangeResult(
    string? Error,
    IReadOnlyList<string> Fields,
    Guid? PublicId,
    byte[]? Version,
    int? TimeOffId,
    IReadOnlyList<string> Appointments)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No service type with that id in the caller's institution.</summary>
    public const string ServiceTypeNotFound = "service_type_not_found";

    /// <summary>No leader with that id in the caller's institution.</summary>
    public const string LeaderNotFound = "leader_not_found";

    /// <summary>No leave with that id in the caller's institution.</summary>
    public const string TimeOffNotFound = "time_off_not_found";

    /// <summary>The institution already offers a service by that name (<c>UQ_ServiceTypes_Name</c>).</summary>
    public const string NameTaken = "service_name_taken";

    /// <summary>No account has the phone number given for linking.</summary>
    public const string AccountNotFound = "account_not_found";

    /// <summary>The account is already another leader of this institution (<c>UX_ReligiousLeaders_User</c>).</summary>
    public const string AccountAlreadyLinked = "account_already_linked";

    /// <summary>The leave overlaps leave already recorded for the leader.</summary>
    public const string TimeOffOverlaps = "time_off_overlaps";

    /// <summary>Someone changed it since it was read; read it again.</summary>
    public const string Stale = "stale";

    public bool Succeeded => Error is null;

    internal static SetupChangeResult Ok() => new(null, [], null, null, null, []);

    internal static SetupChangeResult For(Guid publicId, byte[] version, IReadOnlyList<string>? appointments = null) =>
        new(null, [], publicId, version, null, appointments ?? []);

    internal static SetupChangeResult ForTimeOff(int id) => new(null, [], null, null, id, []);

    internal static SetupChangeResult Failed(string error, IReadOnlyList<string>? appointments = null) =>
        new(error, [], null, null, null, appointments ?? []);

    internal static SetupChangeResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null, null, []);
}
