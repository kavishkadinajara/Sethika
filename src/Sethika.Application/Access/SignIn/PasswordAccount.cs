namespace Sethika.Application.Access.SignIn;

/// <summary>The fields of <c>auth.Users</c> that checking a password and starting a session read.</summary>
public sealed record PasswordAccount(
    Guid Id,
    string? PasswordHash,
    DateTimeOffset? LockoutEnd,
    string Status,
    string UserType,
    string SecurityStamp);
