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
/// </remarks>
public interface IPhoneNumberNormalizer
{
    /// <summary>
    /// Converts <paramref name="phoneNumber"/> to E.164, or returns false if it is not a valid
    /// number that could receive a text message.
    /// </summary>
    /// <param name="phoneNumber">The number as the user typed it.</param>
    /// <param name="normalized">The E.164 form, for example <c>+94771234567</c>.</param>
    /// <returns>True when the number is valid and <paramref name="normalized"/> is set.</returns>
    bool TryNormalize(string? phoneNumber, out string normalized);
}
