namespace Sethika.Application.Services.Appointments;

/// <summary>The outcome of a change to an appointment: its status after it, or why not.</summary>
/// <param name="Error">An API error code (<c>ServiceErrors</c> or one of the constants here); <see langword="null"/> on success.</param>
/// <param name="Leaders">For <c>leader_not_free</c>: the leaders who are not, by name, when known.</param>
public sealed record AppointmentResult(string? Error, string? Status, IReadOnlyList<string> Leaders)
{
    /// <summary>No appointment with that number that the caller may act on.</summary>
    public const string NotFound = "appointment_not_found";

    /// <summary>Someone else changed the appointment first; read it again.</summary>
    public const string Stale = "stale";

    /// <summary>The request named no leader, or too long a reason.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>The signed-in account is not an active leader on this institution's roster (V21).</summary>
    public const string NotALeader = "not_a_leader";

    public bool Succeeded => Error is null;

    internal static AppointmentResult Ok(string status) => new(null, status, []);

    internal static AppointmentResult Failed(string error, IReadOnlyList<string>? leaders = null) => new(error, null, leaders ?? []);
}
