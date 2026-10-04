using System.Text;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Asks for a one-time code: <c>signup</c> (the default) or <c>login</c>. A sign-in code is sent in
/// the account's own language, so <c>Language</c> applies to signup codes only.
/// </summary>
public sealed record OtpRequestBody(string? PhoneNumber, string? Purpose, string? Language);

/// <summary>Presents a one-time code.</summary>
public sealed record OtpVerifyBody(string? PhoneNumber, string? Purpose, string? Code)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PhoneNumber = ").Append(PhoneNumber).Append(", Purpose = ").Append(Purpose);
        return true;
    }
}

/// <summary>Signs in with a code sent by <c>/otp/request</c> with purpose <c>login</c>.</summary>
public sealed record OtpSignInBody(string? PhoneNumber, string? Code)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PhoneNumber = ").Append(PhoneNumber).Append(", Code = ").Append(Code is null ? "null" : "(redacted)");
        return true;
    }
}

/// <summary>Creates a donor account.</summary>
public sealed record SignUpBody(
    string? PhoneNumber,
    string? VerificationTicket,
    string? FullName,
    string? Email,
    string? Password,
    string? PreferredLanguage,
    IReadOnlyCollection<string>? AcceptedDocumentHashes)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PhoneNumber = ").Append(PhoneNumber).Append(", Email = ").Append(Email);
        return true;
    }
}

/// <summary>Signs in with a phone number or email and a password.</summary>
public sealed record LoginBody(string? Login, string? Password)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Login = ").Append(Login);
        return true;
    }
}

/// <summary>A step-up code. The phone it was sent to is the account's, so it is not in the body.</summary>
public sealed record StepUpVerifyBody(string? Code)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = ").Append(Code is null ? "null" : "(redacted)");
        return true;
    }
}

/// <summary>Chooses the institution the session acts for, by slug.</summary>
public sealed record SwitchInstitutionBody(string? Institution);

/// <summary>An institution the user may act for, by slug - internal ids are never sent (CLAUDE.md).</summary>
public sealed record InstitutionChoice(string Slug, string Name);

/// <summary>What a new session returns. The refresh token goes in a cookie, never in the body.</summary>
public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    IReadOnlyList<InstitutionChoice> Institutions,
    string? ActiveInstitution);
