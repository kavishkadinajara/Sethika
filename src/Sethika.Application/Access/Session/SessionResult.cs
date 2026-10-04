using System.Text;

namespace Sethika.Application.Access.Session;

/// <summary>A refresh, optionally switching the institution the session acts for.</summary>
/// <param name="RefreshToken">The token from the httpOnly cookie.</param>
/// <param name="SwitchToInstitution">
/// The slug to act for from now on, or null to keep the current one. Checked against
/// <c>auth.UserRoles</c>; never taken on trust.
/// </param>
/// <param name="IpAddress">For the new refresh token row.</param>
/// <param name="UserAgent">For the new refresh token row.</param>
public sealed record RefreshCommand(string? RefreshToken, string? SwitchToInstitution, string? IpAddress, string? UserAgent)
{
    /// <summary>Leaves the refresh token - a bearer secret for two weeks - out of <see cref="object.ToString"/>.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("SwitchToInstitution = ").Append(SwitchToInstitution);
        return true;
    }
}

/// <summary>Why a refresh was refused.</summary>
public enum SessionFailure
{
    /// <summary>Rotated; new tokens issued.</summary>
    None = 0,

    /// <summary>Unknown, expired, revoked, or issued before the user's security stamp last changed.</summary>
    Invalid = 1,

    /// <summary>
    /// A token that had already been rotated was presented again - a copy is in someone else's
    /// hands. The whole family has been revoked.
    /// </summary>
    ReuseDetected = 2,

    /// <summary>The account is suspended, deactivated or deleted. The family has been revoked.</summary>
    AccountNotActive = 3,

    /// <summary>
    /// The user holds no role in the institution asked for. Nothing was rotated and no token issued:
    /// knowing an institution's slug must not be a way to act for it.
    /// </summary>
    InstitutionNotAvailable = 4,

    /// <summary>
    /// Another request presenting the same token rotated it moments ago - two tabs refreshing at
    /// once. Nothing issued, nothing revoked. The browser holds the successor, or is about to, so
    /// its cookies must be left alone and the client retries with them. Only while that successor's
    /// session is alive: once it has ended, the answer is <see cref="Invalid"/>.
    /// </summary>
    Superseded = 5,
}

/// <summary>The outcome of a refresh.</summary>
public sealed record SessionResult(
    SessionFailure Failure,
    IssuedTokens? Tokens,
    IReadOnlyList<InstitutionAccess> Institutions,
    InstitutionAccess? ActiveInstitution)
{
    /// <summary>True when the token was rotated and a new pair issued.</summary>
    public bool Succeeded => Failure == SessionFailure.None;

    /// <summary>Rotated.</summary>
    public static SessionResult Success(
        IssuedTokens tokens,
        IReadOnlyList<InstitutionAccess> institutions,
        InstitutionAccess? activeInstitution) =>
        new(SessionFailure.None, tokens, institutions, activeInstitution);

    /// <summary>Refused, for <paramref name="failure"/>.</summary>
    public static SessionResult Failed(SessionFailure failure) => new(failure, null, [], null);
}
