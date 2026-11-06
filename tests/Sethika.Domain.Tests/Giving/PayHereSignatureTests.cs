using System.Globalization;
using Sethika.Domain.Giving;

namespace Sethika.Domain.Tests.Giving;

/// <summary>
/// PayHere's two signatures against fixed vectors (plan 09, §4). The expected values were computed outside
/// this code, with Python's hashlib, from the formula on PayHere's Checkout API page.
/// </summary>
public sealed class PayHereSignatureTests
{
    private const string MerchantId = "1211149";
    private const string OrderId = "ItemNo12345";
    private const string Secret = "MzkxNTY4OTAxMjM0NTY3ODkw";

    [Fact]
    public void TheCheckoutHash_IsPayHeresDocumentedFormula() =>
        Assert.Equal("889A9B391E969ABA89329C6207965CC6", PayHereSignature.Checkout(MerchantId, OrderId, 1000m, "LKR", Secret));

    [Fact]
    public void TheNotificationSignature_IsPayHeresDocumentedFormula() =>
        Assert.Equal("0A8D5B313D07CA7983C2563E400044BF", PayHereSignature.Notification(MerchantId, OrderId, "1000.00", "LKR", "2", Secret));

    [Theory]
    [InlineData("1000", "1000.00")]
    [InlineData("1000.5", "1000.50")]
    [InlineData("100.25", "100.25")]
    [InlineData("1000000", "1000000.00")]
    public void AnAmount_HasTwoDecimalsAndNoSeparators(string value, string expected) =>
        Assert.Equal(expected, PayHereSignature.Amount(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Fact]
    public void AnAmount_IsTheSameInACultureWithADecimalComma()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("1234.50", PayHereSignature.Amount(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("0A8D5B313D07CA7983C2563E400044BF", true)]
    [InlineData("0a8d5b313d07ca7983c2563e400044bf", false)]
    [InlineData("0A8D5B313D07CA7983C2563E400044BE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_TakesOnlyTheExactSignature(string? received, bool expected) =>
        Assert.Equal(expected, PayHereSignature.Matches("0A8D5B313D07CA7983C2563E400044BF", received));
}
