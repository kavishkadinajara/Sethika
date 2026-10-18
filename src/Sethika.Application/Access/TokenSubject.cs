namespace Sethika.Application.Access;

/// <summary>Who a pair of tokens is issued to, and the tenant the session acts for.</summary>
/// <param name="UserId">The user, from <c>auth.Users.Id</c>.</param>
/// <param name="InstitutionId">
/// The institution the session acts for, or null when it acts for none - a donor, or a user who has
/// not yet chosen one. This value ends up in the database session context and so decides what
/// Row-Level Security lets the session see; it must already have been checked against
/// <c>auth.UserRoles</c>.
/// </param>
/// <param name="IsPlatform">
/// True for platform staff, whose sessions RLS lets across tenants. Taken from the user record,
/// never from anything the client sent.
/// </param>
public sealed record TokenSubject(Guid UserId, int? InstitutionId, bool IsPlatform);
