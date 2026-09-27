using System.Text;

namespace Sethika.Application.Access.SignUp;

/// <summary>A donor creating an account, after verifying their phone.</summary>
/// <param name="PhoneNumber">The verified number, in any common spelling.</param>
/// <param name="VerificationTicket">The ticket the OTP verification issued for this number.</param>
/// <param name="FullName">As the donor wants it on receipts and the gratitude wall.</param>
/// <param name="Email">Optional.</param>
/// <param name="Password">The chosen password, checked against policy before anything is written.</param>
/// <param name="PreferredLanguage"><c>en</c>, <c>si</c> or <c>ta</c>; <c>en</c> when absent.</param>
/// <param name="AcceptedDocumentHashes">
/// The <c>ContentSha256</c> of each legal document the client showed and the donor accepted. Consent
/// is recorded against exactly the text whose hash was presented, so if the terms change between
/// display and submit the signup is refused rather than recorded against words nobody saw.
/// </param>
/// <param name="IpAddress">For the consent record.</param>
/// <param name="UserAgent">For the consent record.</param>
public sealed record SignUpDonorCommand(
    string? PhoneNumber,
    string? VerificationTicket,
    string? FullName,
    string? Email,
    string? Password,
    string? PreferredLanguage,
    IReadOnlyCollection<string>? AcceptedDocumentHashes,
    string? IpAddress,
    string? UserAgent)
{
    /// <summary>Leaves the password and the ticket - both secrets - out of <see cref="object.ToString"/>.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PhoneNumber = ").Append(PhoneNumber)
               .Append(", FullName = ").Append(FullName)
               .Append(", Email = ").Append(Email)
               .Append(", PreferredLanguage = ").Append(PreferredLanguage);

        return true;
    }
}
