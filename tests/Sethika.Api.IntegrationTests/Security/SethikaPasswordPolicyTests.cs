using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sethika.Application.Access;
using Sethika.Infrastructure.Security;
using Xunit;

namespace Sethika.Api.IntegrationTests.Security;

public sealed class SethikaPasswordPolicyTests
{
    /// <summary>
    /// Builds the policy over Identity's defaults, so these tests describe the shipped
    /// configuration rather than one invented for the test.
    /// </summary>
    private static SethikaPasswordPolicy CreatePolicy(Action<PasswordOptions>? configure = null)
    {
        var identityOptions = new IdentityOptions();
        configure?.Invoke(identityOptions.Password);

        return new SethikaPasswordPolicy(Options.Create(identityOptions));
    }

    [Fact]
    public void AcceptsAPasswordThatMeetsEveryRule()
    {
        var result = CreatePolicy().Validate("Dana!2026Sethika");

        Assert.True(result.IsValid);
        Assert.Equal(PasswordFailure.None, result.Failures);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsAMissingPasswordAsTooShort(string? password)
    {
        var result = CreatePolicy().Validate(password);

        Assert.False(result.IsValid);
        Assert.True(result.Failures.HasFlag(PasswordFailure.TooShort));
    }

    [Fact]
    public void ReportsEveryBrokenRuleAtOnceRatherThanTheFirst()
    {
        // "abc" breaks four rules. Reporting one at a time makes a user resubmit repeatedly and
        // learn the policy by trial and error, so all four must come back together.
        var result = CreatePolicy().Validate("abc");

        Assert.False(result.IsValid);
        Assert.True(result.Failures.HasFlag(PasswordFailure.TooShort));
        Assert.True(result.Failures.HasFlag(PasswordFailure.MissingUppercase));
        Assert.True(result.Failures.HasFlag(PasswordFailure.MissingDigit));
        Assert.True(result.Failures.HasFlag(PasswordFailure.MissingNonAlphanumeric));
    }

    [Fact]
    public void RejectsAPasswordLongerThanTheCap()
    {
        var result = CreatePolicy().Validate("Aa1!" + new string('x', SethikaPasswordPolicy.MaxLength));

        Assert.False(result.IsValid);
        Assert.True(result.Failures.HasFlag(PasswordFailure.TooLong));
    }

    [Fact]
    public void AcceptsAPassphraseExactlyAtTheCap()
    {
        // The cap is a denial-of-service control, so it must not reject a long passphrase that is
        // within it - off-by-one here would quietly ban legitimate strong passwords.
        var atCap = "Aa1!" + new string('x', SethikaPasswordPolicy.MaxLength - 4);

        Assert.Equal(SethikaPasswordPolicy.MaxLength, atCap.Length);
        Assert.True(CreatePolicy().Validate(atCap).IsValid);
    }

    [Fact]
    public void HonoursRelaxedOptionsRatherThanHardCodingTheRules()
    {
        // Proves the policy reads Identity's options instead of embedding its own constants.
        var result = CreatePolicy(p =>
        {
            p.RequireUppercase = false;
            p.RequireNonAlphanumeric = false;
            p.RequiredLength = 8;
        }).Validate("dana2026");

        Assert.True(result.IsValid);
    }

    [Theory]
    // The account is +94771234567. Each of these embeds a run of the number, in a form a user
    // would plausibly choose, and each must be refused.
    [InlineData("Sethika771234567!")]
    [InlineData("Dana0771234567!")]
    [InlineData("My94771234567Pass!")]
    public void RejectsAPasswordContainingTheOwnersPhoneNumber(string password)
    {
        var result = CreatePolicy().Validate(password, ["+94771234567"]);

        Assert.False(result.IsValid);
        Assert.True(result.Failures.HasFlag(PasswordFailure.ContainsPersonalData));
    }

    [Fact]
    public void CatchesThePhoneNumberEvenWhenTheStoredFormIsSpacedDifferently()
    {
        // Stored as "+94 77 123 4567", typed into the password as "0771234567". Comparing the raw
        // strings would miss this, so both sides reduce to digits before comparison.
        var result = CreatePolicy().Validate("Dana0771234567!", ["+94 77 123 4567"]);

        Assert.True(result.Failures.HasFlag(PasswordFailure.ContainsPersonalData));
    }

    [Fact]
    public void RejectsAPasswordContainingTheOwnersEmailLocalPart()
    {
        var result = CreatePolicy().Validate("Kavishka2026!", ["kavishka@example.com"]);

        Assert.True(result.Failures.HasFlag(PasswordFailure.ContainsPersonalData));
    }

    [Fact]
    public void ComparesPersonalDataWithoutRegardToCase()
    {
        var result = CreatePolicy().Validate("KAVISHKA2026!", ["kavishka@example.com"]);

        Assert.True(result.Failures.HasFlag(PasswordFailure.ContainsPersonalData));
    }

    [Fact]
    public void DoesNotRejectAPasswordOverAShortCoincidentalOverlap()
    {
        // "Dana" appears in both, but a four-character overlap is a coincidence, not the user's
        // phone number. Rejecting on it would refuse reasonable passphrases and train users to
        // pick worse ones.
        var result = CreatePolicy().Validate("DanaSethika2026!", ["Dana Perera"]);

        Assert.False(result.Failures.HasFlag(PasswordFailure.ContainsPersonalData));
    }

    [Fact]
    public void IgnoresNullAndBlankPersonalDataEntries()
    {
        // A user with no email means a null in the list; it must not throw or match everything.
        var result = CreatePolicy().Validate("Dana!2026Sethika", [null, "", "   "]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AppliesNoPersonalDataRuleWhenNoneIsSupplied()
    {
        Assert.True(CreatePolicy().Validate("Dana!2026Sethika").IsValid);
    }
}
