namespace Sethika.Application.Access;

/// <summary>
/// The one place an email address is reduced to the form stored in <c>auth.Users.NormalizedEmail</c>.
/// </summary>
/// <remarks>
/// <c>UX_Users_NormalizedEmail</c> only prevents duplicate accounts if every write and every lookup
/// normalises identically. <c>UserManager</c> would do this for us; plan A1 moved it here, so signup,
/// email change and sign-in all call this and nothing else. Upper-invariant, as Identity does, so a
/// Turkish-locale server does not turn <c>i</c> into a dotted capital and miss the account.
/// </remarks>
public static class NormalizedEmail
{
    /// <summary>Trims and upper-cases <paramref name="email"/> culture-invariantly.</summary>
    public static string From(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Trim().ToUpperInvariant();
    }
}
