using System.Text;

namespace Sethika.Api.Endpoints;

/// <summary>Asks for a one-time code. Only <c>signup</c> is accepted here.</summary>
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
