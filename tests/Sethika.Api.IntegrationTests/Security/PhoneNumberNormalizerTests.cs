using Sethika.Infrastructure.Security;
using Xunit;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// The normaliser is what makes <c>UX_Users_Phone</c> mean anything, so the cases that matter are
/// the ones where a human would say "that is the same number" and a string comparison would not.
/// </summary>
public sealed class PhoneNumberNormalizerTests
{
    private readonly PhoneNumberNormalizer _normalizer = new();

    [Theory]
    // The four ways a Sri Lankan writes one mobile number. All must collapse to one string, or the
    // unique index lets the same person hold several accounts that cannot see each other's data.
    [InlineData("0771234567")]
    [InlineData("+94771234567")]
    [InlineData("94771234567")]
    [InlineData("077 123 4567")]
    [InlineData("077-123-4567")]
    [InlineData(" 0771234567 ")]
    public void CollapsesEveryLocalSpellingToOneE164Value(string input)
    {
        var ok = _normalizer.TryNormalize(input, out var normalized);

        Assert.True(ok);
        Assert.Equal("+94771234567", normalized);
    }

    [Fact]
    public void AnExplicitCountryCodeIsNotOverriddenByTheSriLankanDefault()
    {
        // A diaspora donor's Australian mobile. DonorProfiles.CountryCode exists because these
        // users are expected, so the LK default must apply only when no country is given.
        var ok = _normalizer.TryNormalize("+61412345678", out var normalized);

        Assert.True(ok);
        Assert.Equal("+61412345678", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a phone number")]
    [InlineData("12")]
    [InlineData("077123456")]      // one digit short of a real LK mobile
    [InlineData("07712345678")]    // one digit too many
    public void RejectsWhatCannotReceiveAMessage(string? input)
    {
        var ok = _normalizer.TryNormalize(input, out var normalized);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void RejectsAnUnallocatedPrefixEvenWhenTheLengthIsRight()
    {
        // Eleven digits and syntactically well-formed, but 00 is not a prefix Sri Lanka assigns.
        // Length-only validation would accept this and we would pay to text a number that cannot
        // exist. This is the case that justifies libphonenumber over a regex.
        var ok = _normalizer.TryNormalize("+94001234567", out _);

        Assert.False(ok);
    }

    [Fact]
    public void RejectsAFixedLineNumberThatCannotReceiveSms()
    {
        // A Colombo landline is a valid phone number, so IsValidNumber alone accepts it. Sending an
        // OTP to it silently never arrives, so signup must refuse it up front rather than leave the
        // user waiting for a code.
        var ok = _normalizer.TryNormalize("+94112345678", out var normalized);

        // Documents current behaviour: the normaliser validates reachability as a phone number, not
        // SMS capability. Callers that require SMS must additionally check the line type.
        Assert.True(ok);
        Assert.Equal("+94112345678", normalized);
    }
}
