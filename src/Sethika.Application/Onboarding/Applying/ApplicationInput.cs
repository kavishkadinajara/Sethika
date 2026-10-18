using System.Net.Mail;
using System.Text.RegularExpressions;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Onboarding.Applying;

/// <summary>
/// Checks and tidies what an applicant typed, field by field, before any of it reaches the
/// aggregate (plan 02, 5.3). Pure apart from the phone normaliser, so every rule is unit-tested.
/// </summary>
/// <remarks>
/// The limits are the columns' own (<c>org.RegistrationRequests</c>,
/// <c>org.RegistrationRequestOfficeBearers</c>), so a value accepted here is never cut or refused
/// by the database. Field names in the result are the API's, so the client can point at each one.
/// </remarks>
public static partial class ApplicationInput
{
    /// <summary>More than enough for a temple society's committee; bounds the request.</summary>
    public const int MaxOfficeBearers = 15;

    /// <summary>The tidied details, and the fields that are missing or malformed.</summary>
    public static (ApplicationDetails Details, IReadOnlyList<string> Invalid) Check(
        ApplicationDetails details,
        IPhoneNumberNormalizer phones,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(phones);

        var invalid = new List<string>();

        var tidy = details with
        {
            InstitutionType = Required(details.InstitutionType, 30, "institutionType", invalid)?.ToUpperInvariant(),
            ProposedName = Required(details.ProposedName, 200, "proposedName", invalid),
            NameSi = Optional(details.NameSi, 200, "nameSi", invalid),
            NameTa = Optional(details.NameTa, 200, "nameTa", invalid),
            OfficialRegistrationNo = Ascii(Required(details.OfficialRegistrationNo, 50, "officialRegistrationNo", invalid), "officialRegistrationNo", invalid),
            RegisteringAuthority = Required(details.RegisteringAuthority, 200, "registeringAuthority", invalid),
            Description = Optional(details.Description, 2000, "description", invalid),
            AddressLine1 = Required(details.AddressLine1, 200, "addressLine1", invalid),
            AddressLine2 = Optional(details.AddressLine2, 200, "addressLine2", invalid),
            City = Required(details.City, 100, "city", invalid),
            District = Required(details.District, 10, "district", invalid)?.ToUpperInvariant(),
            GnDivision = Optional(details.GnDivision, 100, "gnDivision", invalid),
            PostalCode = Optional(details.PostalCode, 10, "postalCode", invalid),
            OfficePhone = Phone(details.OfficePhone, required: true, "officePhone", phones, invalid),
            OfficeEmail = Email(details.OfficeEmail, "officeEmail", invalid),
            Website = Website(details.Website, invalid),
            ApplicantFullName = Required(details.ApplicantFullName, 150, "applicantFullName", invalid),
            ApplicantNic = Nic(details.ApplicantNic, "applicantNic", invalid),
            ApplicantDesignation = Required(details.ApplicantDesignation, 100, "applicantDesignation", invalid),
            ApplicantPhone = Phone(details.ApplicantPhone, required: true, "applicantPhone", phones, invalid),
            ApplicantEmail = Email(details.ApplicantEmail, "applicantEmail", invalid),
        };

        if (tidy.PostalCode is { } postal && !postal.All(char.IsAsciiDigit))
        {
            invalid.Add("postalCode");
        }

        if (details.RegistrationDate is not { } registered || registered > today || registered.Year < 1800)
        {
            invalid.Add("registrationDate");
        }

        if (details.EstablishedYear is { } year && (year < 1 || year > today.Year))
        {
            invalid.Add("establishedYear");
        }

        if (details.ResidentCapacity is <= 0)
        {
            invalid.Add("residentCapacity");
        }

        // CK_RegReq_Latitude and CK_RegReq_Longitude: Sri Lanka's bounding box.
        if (details.Latitude is not (>= 5.5m and <= 10.0m))
        {
            invalid.Add("latitude");
        }

        if (details.Longitude is not (>= 79.3m and <= 82.1m))
        {
            invalid.Add("longitude");
        }

        return (tidy, invalid);
    }

    /// <summary>The tidied office bearers, and the fields that are missing or malformed: <c>officeBearers[1].nic</c>.</summary>
    public static (IReadOnlyList<OfficeBearerDetails> Bearers, IReadOnlyList<string> Invalid) Check(
        IReadOnlyList<OfficeBearerDetails>? bearers,
        IPhoneNumberNormalizer phones)
    {
        ArgumentNullException.ThrowIfNull(phones);

        var invalid = new List<string>();
        if (bearers is null || bearers.Count > MaxOfficeBearers)
        {
            return ([], ["officeBearers"]);
        }

        var tidy = new List<OfficeBearerDetails>(bearers.Count);
        for (var i = 0; i < bearers.Count; i++)
        {
            var b = bearers[i];
            var at = $"officeBearers[{i}].";
            tidy.Add(b with
            {
                FullName = Required(b.FullName, 150, at + "fullName", invalid),
                Nic = Nic(b.Nic, at + "nic", invalid),
                Designation = Required(b.Designation, 100, at + "designation", invalid),
                Phone = Phone(b.Phone, required: true, at + "phone", phones, invalid),
                Email = Email(b.Email, at + "email", invalid),
            });
        }

        return (tidy, invalid);
    }

    private static string? Required(string? value, int maxLength, string field, List<string> invalid)
    {
        var tidy = value?.Trim();
        if (string.IsNullOrEmpty(tidy) || tidy.Length > maxLength)
        {
            invalid.Add(field);
            return null;
        }

        return tidy;
    }

    private static string? Optional(string? value, int maxLength, string field, List<string> invalid)
    {
        var tidy = value?.Trim();
        if (string.IsNullOrEmpty(tidy))
        {
            return null;
        }

        if (tidy.Length > maxLength)
        {
            invalid.Add(field);
            return null;
        }

        return tidy;
    }

    /// <summary>A <c>varchar</c> column turns anything outside ASCII into <c>?</c>, so refuse it instead.</summary>
    private static string? Ascii(string? value, string field, List<string> invalid)
    {
        if (value is not null && !value.All(c => char.IsAscii(c) && !char.IsControl(c)))
        {
            invalid.Add(field);
            return null;
        }

        return value;
    }

    /// <summary>A Sri Lankan NIC: nine digits and V or X (before 2016), or twelve digits.</summary>
    private static string? Nic(string? value, string field, List<string> invalid)
    {
        var tidy = value?.Trim().ToUpperInvariant();
        if (tidy is null || !NicShape().IsMatch(tidy))
        {
            invalid.Add(field);
            return null;
        }

        return tidy;
    }

    private static string? Phone(string? value, bool required, string field, IPhoneNumberNormalizer phones, List<string> invalid)
    {
        if (string.IsNullOrWhiteSpace(value) && !required)
        {
            return null;
        }

        if (!phones.TryNormalize(value, out var normalized))
        {
            invalid.Add(field);
            return null;
        }

        return normalized;
    }

    /// <summary>A bare address, not a display-name form such as <c>Name &lt;a@b.lk&gt;</c>.</summary>
    private static string? Email(string? value, string field, List<string> invalid)
    {
        var tidy = value?.Trim();
        if (string.IsNullOrEmpty(tidy))
        {
            return null;
        }

        if (tidy.Length > 256 || !MailAddress.TryCreate(tidy, out var parsed) || !string.Equals(parsed.Address, tidy, StringComparison.Ordinal))
        {
            invalid.Add(field);
            return null;
        }

        return tidy;
    }

    /// <summary>An absolute http or https address; a <c>javascript:</c> link would be shown to reviewers.</summary>
    private static string? Website(string? value, List<string> invalid)
    {
        var tidy = value?.Trim();
        if (string.IsNullOrEmpty(tidy))
        {
            return null;
        }

        if (tidy.Length > 200 || !Uri.TryCreate(tidy, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            invalid.Add("website");
            return null;
        }

        return tidy;
    }

    [GeneratedRegex("^([0-9]{9}[VX]|[0-9]{12})$", RegexOptions.CultureInvariant)]
    private static partial Regex NicShape();
}
