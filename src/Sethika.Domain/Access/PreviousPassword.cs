namespace Sethika.Domain.Access;

/// <summary>
/// A password hash a user has replaced. Maps <c>auth.PasswordHistory</c>.
/// </summary>
/// <remarks>
/// Named for the row, as <see cref="LoginAttempt"/> is: each row is one earlier password.
/// </remarks>
public class PreviousPassword
{
    public long Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The hash as it was stored on the account, never the password.</summary>
    public string PasswordHash { get; set; } = null!;

    /// <summary>When it stopped being the account's password.</summary>
    public DateTime CreatedAt { get; set; }
}
