using System.Text;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Asks for a one-time code: <c>signup</c> (the default), <c>reset</c> or <c>login</c>. Reset and
/// sign-in codes are sent in the account's own language, so <c>Language</c> applies to signup codes
/// only.
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

/// <summary>Sets a new password with the ticket a verified <c>reset</c> code returned.</summary>
public sealed record PasswordResetBody(string? Ticket, string? NewPassword)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Ticket = ").Append(Ticket is null ? "null" : "(redacted)");
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

/// <summary>
/// What a new mobile session returns (plan F3): the web's <see cref="SessionResponse"/> plus the
/// refresh token, which the app keeps in the platform's secure store.
/// </summary>
public sealed record MobileSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    IReadOnlyList<InstitutionChoice> Institutions,
    string? ActiveInstitution);

/// <summary>A mobile session's refresh token, for refresh and logout.</summary>
public sealed record MobileRefreshBody(string? RefreshToken)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RefreshToken = ").Append(RefreshToken is null ? "null" : "(redacted)");
        return true;
    }
}

/// <summary>Moves a mobile session onto another institution, by slug.</summary>
public sealed record MobileSwitchInstitutionBody(string? RefreshToken, string? Institution)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RefreshToken = ").Append(RefreshToken is null ? "null" : "(redacted)")
            .Append(", Institution = ").Append(Institution);
        return true;
    }
}
