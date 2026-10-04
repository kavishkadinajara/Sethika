namespace Sethika.Application.Access.SignIn;

/// <summary>A user who has proved who they are, about to be given a session.</summary>
/// <param name="UserId">The account.</param>
/// <param name="UserType">Decides the platform flag in the token.</param>
/// <param name="SecurityStamp">Bound into the refresh family, so a later rotation ends it.</param>
/// <param name="LoginName">What they signed in with, normalised, for <c>auth.LoginHistory</c>.</param>
/// <param name="IpAddress">The caller's address.</param>
/// <param name="UserAgent">The caller's user agent.</param>
internal sealed record SessionStart(
    Guid UserId,
    string UserType,
    string SecurityStamp,
    string LoginName,
    string? IpAddress,
    string? UserAgent);
