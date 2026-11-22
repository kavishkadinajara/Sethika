using PhoneNumbers;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Reduces a phone number as typed to E.164 using libphonenumber's metadata.
/// </summary>
/// <remarks>
/// Sri Lanka is the default region, so a local <c>0771234567</c> parses without a country code,
/// but an explicit <c>+</c> prefix always wins - a diaspora donor entering an Australian number
/// must not have it read as Sri Lankan.
///
/// Validity is checked with <c>IsValidNumber</c>, which tests the number against the ranges the
/// country actually assigns, not merely its length. <c>+94001234567</c> is eleven digits and
/// syntactically fine, but 00 is not an allocated Sri Lankan prefix, so it can never receive a
/// text message and is rejected here rather than after paying to send to it.
///
/// A valid number is not always a textable one: <c>+94112345678</c> is a real Colombo landline.
/// <see cref="TryNormalizeMobile"/> adds the line-type check for every number a code is sent to.
/// </remarks>
public sealed class PhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private const string DefaultRegion = "LK";

    // The parser reads embedded metadata and is documented as thread-safe, so one instance is
    // shared rather than reloading that metadata on every signup attempt.
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    // FIXED_LINE_OR_MOBILE is where the plan does not tell the two apart (the US, Canada): such a
    // number may well be a mobile, and refusing it would shut out a diaspora donor in Toronto.
    private static readonly PhoneNumberType[] Textable = [PhoneNumberType.MOBILE, PhoneNumberType.FIXED_LINE_OR_MOBILE];

    public bool TryNormalize(string? phoneNumber, out string normalized) =>
        TryParse(phoneNumber, out _, out normalized);

    public bool TryNormalizeMobile(string? phoneNumber, out string normalized)
    {
        if (TryParse(phoneNumber, out var parsed, out normalized) && Textable.Contains(Util.GetNumberType(parsed)))
        {
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static bool TryParse(string? phoneNumber, out PhoneNumber parsed, out string normalized)
    {
        normalized = string.Empty;
        parsed = null!;

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        try
        {
            parsed = Util.Parse(phoneNumber, DefaultRegion);

            if (!Util.IsValidNumber(parsed))
            {
                return false;
            }

            normalized = Util.Format(parsed, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException)
        {
            // Unparseable input is a validation failure, not an exceptional condition: this runs on
            // a public signup form, so it is reached by ordinary typos and must not surface as a 500.
            return false;
        }
    }
}
