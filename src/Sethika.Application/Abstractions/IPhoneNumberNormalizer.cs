namespace Sethika.Application.Abstractions;

/// <summary>
/// Reduces a phone number as typed to the single canonical form stored in the database.
/// </summary>
/// <remarks>
/// <c>UX_Users_Phone</c> is a unique index over <c>auth.Users.PhoneNumber</c>. A Sri Lankan
/// mobile can be written <c>0771234567</c>, <c>+94771234567</c>, <c>94771234567</c> or with
/// spaces and dashes - one person, several strings. Without normalisation the unique index
/// prevents nothing: the same human signs up three times, and none of the three accounts can see
/// the others' bookings.
///
/// So normalisation happens before any lookup or insert, and E.164 is what is stored.
///
/// Two questions, two methods. <see cref="TryNormalize"/> asks "is this a real phone number?" -
/// right for an office line on an application or a walk-in donor's contact. <see cref="TryNormalizeMobile"/>
/// asks "can this number receive a text?" - right wherever a code or a reminder is going to be
/// texted to it: an account's phone, which signs in with and steps up by a texted code, and a
/// leader's phone (QA DEF-002: a Colombo landline passed the first test and was sent a paid
/// sign-up code that could never arrive).
/// </remarks>
public interface IPhoneNumberNormalizer
{
    /// <summary>
    /// Converts <paramref name="phoneNumber"/> to E.164, or returns false if it is not a number the
    /// country assigns. Any line type: a landline passes.
    /// </summary>
    /// <param name="phoneNumber">The number as the user typed it.</param>
    /// <param name="normalized">The E.164 form, for example <c>+94112345678</c>.</param>
    /// <returns>True when the number is valid and <paramref name="normalized"/> is set.</returns>
    bool TryNormalize(string? phoneNumber, out string normalized);

    /// <summary>
    /// As <see cref="TryNormalize"/>, but only for a number that can receive a text message: a
    /// mobile, or a number whose country does not say which it is (the US and Canada).
    /// </summary>
    /// <param name="phoneNumber">The number as the user typed it.</param>
    /// <param name="normalized">The E.164 form, for example <c>+94771234567</c>.</param>
    /// <returns>True when the number can be texted and <paramref name="normalized"/> is set.</returns>
    bool TryNormalizeMobile(string? phoneNumber, out string normalized);
}
