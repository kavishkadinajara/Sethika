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

    [Theory]
    // A Colombo and a Kandy landline: real numbers, so an office phone may be one.
    [InlineData("0112345678", "+94112345678")]
    [InlineData("+94 81 223 4567", "+94812234567")]
    public void ALandlineIsAValidPhoneNumber(string input, string expected)
    {
        var ok = _normalizer.TryNormalize(input, out var normalized);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    // QA DEF-002: these were sent paid sign-up codes that could never arrive.
    [InlineData("0112345678")]
    [InlineData("0812234567")]
    [InlineData("+94 11 234 5678")]
    public void ALandlineCannotReceiveACode(string input)
    {
        var ok = _normalizer.TryNormalizeMobile(input, out var normalized);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("077 123 4567", "+94771234567")]   // Sri Lankan mobile, typed locally
    [InlineData("+61412345678", "+61412345678")]   // Australian mobile
    [InlineData("+447911123456", "+447911123456")] // UK mobile
    [InlineData("+14165550123", "+14165550123")]   // Toronto: the plan does not say landline or mobile
    public void AMobileCanReceiveACode(string input, string expected)
    {
        var ok = _normalizer.TryNormalizeMobile(input, out var normalized);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("077123456")]
    [InlineData("+94001234567")]
    public void WhatIsNoNumberAtAllIsNoMobileEither(string? input)
    {
        Assert.False(_normalizer.TryNormalizeMobile(input, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }
}
