using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Giving;

/// <summary>
/// Sealing merchant secrets (plan 09, M21) and the start-up check on the Payments section (M10). No SQL
/// or Redis: these are the two classes alone, so a bad key is shown failing at registration.
/// </summary>
public sealed class PaymentConfigurationTests
{
    private static readonly string KeyOne = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
    private static readonly string KeyTwo = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());

    private static AesGcmMerchantSecrets Secrets(string current, params (string Id, string Key)[] keys) =>
        AesGcmMerchantSecrets.Create(new PaymentOptions { CurrentKeyId = current, Keys = keys.ToDictionary(k => k.Id, k => k.Key) });

    [Fact]
    public void ASealedSecret_OpensForItsInstitution()
    {
        var secrets = Secrets("k1", ("k1", KeyOne));

        var sealedSecret = secrets.Seal(7, "merchant-secret-value");

        Assert.Equal("k1", sealedSecret.KeyId);
        Assert.Equal(12, sealedSecret.Nonce.Length);
        Assert.Equal("merchant-secret-value".Length + 16, sealedSecret.Cipher.Length);
        Assert.Equal("merchant-secret-value", secrets.Open(7, sealedSecret));
    }

    [Fact]
    public void ACiphertextCopiedToAnotherInstitution_DoesNotOpen()
    {
        var secrets = Secrets("k1", ("k1", KeyOne));

        var sealedSecret = secrets.Seal(7, "merchant-secret-value");

        Assert.ThrowsAny<CryptographicException>(() => secrets.Open(8, sealedSecret));
    }

    [Fact]
    public void ATamperedCiphertext_DoesNotOpen()
    {
        var secrets = Secrets("k1", ("k1", KeyOne));
        var sealedSecret = secrets.Seal(7, "merchant-secret-value");

        var cipher = sealedSecret.Cipher.ToArray();
        cipher[0] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => secrets.Open(7, sealedSecret with { Cipher = cipher }));
    }

    [Fact]
    public void TheSameSecretSealedTwice_LooksDifferent()
    {
        var secrets = Secrets("k1", ("k1", KeyOne));

        var first = secrets.Seal(7, "merchant-secret-value");
        var second = secrets.Seal(7, "merchant-secret-value");

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Cipher, second.Cipher);
    }

    [Fact]
    public void AfterARotation_TheOldValueStillOpens_AndNewValuesUseTheNewKey()
    {
        var old = Secrets("k1", ("k1", KeyOne)).Seal(7, "merchant-secret-value");

        var rotated = Secrets("k2", ("k1", KeyOne), ("k2", KeyTwo));

        Assert.Equal("merchant-secret-value", rotated.Open(7, old));

        var fresh = rotated.Seal(7, "another");
        Assert.Equal("k2", fresh.KeyId);
        Assert.Equal("another", rotated.Open(7, fresh));
    }

    [Fact]
    public void AValueOfAKeyNoLongerConfigured_SaysWhichKey()
    {
        var old = Secrets("k1", ("k1", KeyOne)).Seal(7, "merchant-secret-value");

        var error = Assert.Throws<InvalidOperationException>(() => Secrets("k2", ("k2", KeyTwo)).Open(7, old));

        Assert.Contains("Payments:Keys:k1", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AHostWithNoCurrentKey_DoesNotStart(string? keyId) =>
        AssertRefused("Payments:CurrentKeyId", Settings(("Payments:CurrentKeyId", keyId)));

    [Fact]
    public void AKeyIdTooLongForTheColumn_StopsTheHost()
    {
        var id = new string('k', 21);

        AssertRefused("Payments:CurrentKeyId", Settings(("Payments:CurrentKeyId", id), ($"Payments:Keys:{id}", KeyOne)));
    }

    [Fact]
    public void AHostWhoseCurrentKeyIsNotListed_DoesNotStart() =>
        AssertRefused("Payments:Keys:other", Settings(("Payments:CurrentKeyId", "other")));

    [Theory]
    [InlineData("c2hvcnQ=")]
    [InlineData("not base64 at all")]
    public void AHostWithABadKey_DoesNotStart(string key) =>
        AssertRefused("Payments:Keys:k1", Settings(("Payments:Keys:k1", key)));

    [Theory]
    [InlineData("Payments:PayHere:ApiPublicUrl", null)]
    [InlineData("Payments:PayHere:WebPublicUrl", "")]
    [InlineData("Payments:PayHere:ApiPublicUrl", "not an address")]
    [InlineData("Payments:PayHere:WebPublicUrl", "http://sethika.example")]
    public void OutsideDevelopment_AnAddressThatIsNotHttps_StopsTheHost(string key, string? value) =>
        AssertRefused(key, Settings((key, value)));

    [Fact]
    public void InDevelopment_AnHttpAddressIsAccepted()
    {
        var site = Register(Settings(("Payments:PayHere:ApiPublicUrl", "http://localhost:3000/")), isDevelopment: true).GetRequiredService<IPayHereSite>();

        Assert.Equal("http://localhost:3000/api/payments/payhere/notify", site.NotifyUrl);
    }

    [Fact]
    public void TheCheckoutAddress_FollowsThePlatformsSandboxSetting()
    {
        Assert.Equal("https://sandbox.payhere.lk/pay/checkout", Register(Settings(), isDevelopment: false).GetRequiredService<IPayHereSite>().CheckoutUrl);
        Assert.Equal(
            "https://www.payhere.lk/pay/checkout",
            Register(Settings(("Payments:PayHere:Sandbox", "false")), isDevelopment: false).GetRequiredService<IPayHereSite>().CheckoutUrl);
    }

    [Theory]
    [InlineData("en", "https://sethika.example/giving/return/019a0000-0000-7000-8000-000000000001")]
    [InlineData("si", "https://sethika.example/si/giving/return/019a0000-0000-7000-8000-000000000001")]
    [InlineData("ta", "https://sethika.example/ta/giving/return/019a0000-0000-7000-8000-000000000001")]
    public void TheReturnPage_IsInTheDonorsLanguage(string language, string expected)
    {
        var site = Register(Settings(), isDevelopment: false).GetRequiredService<IPayHereSite>();
        var order = Guid.Parse("019a0000-0000-7000-8000-000000000001");

        Assert.Equal(expected, site.ReturnUrl(order, language));
        Assert.Equal(expected + "?left=1", site.CancelUrl(order, language));
    }

    private static Dictionary<string, string?> Settings(params (string Key, string? Value)[] changes)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Payments:CurrentKeyId"] = "k1",
            ["Payments:Keys:k1"] = KeyOne,
            ["Payments:PayHere:ApiPublicUrl"] = "https://api.sethika.example/",
            ["Payments:PayHere:WebPublicUrl"] = "https://sethika.example",
        };

        foreach (var (key, value) in changes)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static ServiceProvider Register(Dictionary<string, string?> settings, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddSethikaPayments(configuration, isDevelopment).BuildServiceProvider();
    }

    private static void AssertRefused(string named, Dictionary<string, string?> settings)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(settings, isDevelopment: false));
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }
}
