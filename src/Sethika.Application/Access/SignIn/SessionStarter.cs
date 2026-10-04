using Sethika.Application.Abstractions;
using Sethika.Application.Access.Session;

namespace Sethika.Application.Access.SignIn;

/// <summary>
/// Starts a new session for an account whose password has just been verified: a token pair, the
/// first refresh token of a new family, and the success row in <c>auth.LoginHistory</c>.
/// </summary>
/// <remarks>
/// Sign-in and password change both end in a fresh session, and both go through here, so the session
/// a password change leaves on the calling device is exactly the one a sign-in would have made.
///
/// The rows are added to the context and not saved: each caller saves them inside its own
/// transaction, beside the other writes they must commit or fail with.
/// </remarks>
public sealed class SessionStarter(ISethikaDbContext db, ITokenService tokens)
{
    private const string PlatformStaffUserType = "PlatformStaff";

    /// <summary>Issues the pair and adds its refresh-token and history rows to the context.</summary>
    /// <param name="account">The account, with the stamp every later refresh must still match.</param>
    /// <param name="loginName">What the history row records the sign-in against.</param>
    /// <param name="preferredInstitutionId">
    /// The institution to keep acting for, if the user still holds it. Null at sign-in.
    /// </param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="now">When the session starts.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    public async Task<SignInResult> StartAsync(
        PasswordAccount account,
        string loginName,
        int? preferredInstitutionId,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var institutions = await UserInstitutions.LoadAsync(db, account.Id, now, cancellationToken).ConfigureAwait(false);

        // One institution is the common case - an owner with one temple - and a chooser screen with
        // one option is a pointless click. With several, the token acts for none until the user
        // picks one through switch-institution, which re-checks it.
        var activeInstitution = institutions.FirstOrDefault(i => i.Id == preferredInstitutionId)
                                ?? (institutions.Count == 1 ? institutions[0] : null);
        var activeInstitutionId = activeInstitution?.Id;

        var issued = tokens.Issue(new TokenSubject(
            account.Id,
            activeInstitutionId,
            IsPlatform: string.Equals(account.UserType, PlatformStaffUserType, StringComparison.Ordinal)));

        // A new family: every refresh from here on rotates within it, and a replayed member ends it.
        db.RefreshTokens.Add(RefreshTokenRows.New(
            issued,
            account.Id,
            familyId: Guid.NewGuid(),
            activeInstitutionId,
            account.SecurityStamp,
            ipAddress,
            userAgent,
            now));

        db.LoginAttempts.Add(LoginAttempts.Succeeded(account.Id, loginName, ipAddress, userAgent, now));

        return SignInResult.Success(issued, institutions, activeInstitution);
    }
}
