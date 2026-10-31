namespace Sethika.Domain.Access;

/// <summary>
/// One sign-in attempt, successful or not. Maps <c>auth.LoginHistory</c>.
/// </summary>
/// <remarks>
/// Named for the row rather than the table: each row is one attempt, and "history" is what the
/// table is, not what a row is.
/// </remarks>
public class LoginAttempt
{
    public long Id { get; set; }

    /// <summary>Null when the login matched no account.</summary>
    public Guid? UserId { get; set; }

    public string LoginName { get; set; } = null!;
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public bool MfaUsed { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime AttemptedAt { get; set; }
}
