namespace Sethika.Infrastructure.Security;

/// <summary>
/// The claims that carry the RLS session context in an access token.
/// </summary>
/// <remarks>
/// Lives beside <see cref="JwtTokenService"/>, which writes them, rather than in the Api, which
/// reads them: the Api references Infrastructure and not the reverse, and one definition used by
/// both sides is what stops the writer and the reader drifting apart.
/// </remarks>
public static class SethikaClaimTypes
{
    public const string UserId = "sethika:uid";
    public const string InstitutionId = "sethika:inst";
    public const string IsPlatform = "sethika:platform";
}
