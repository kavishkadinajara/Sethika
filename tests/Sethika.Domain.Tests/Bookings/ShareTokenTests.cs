using System.Security.Cryptography;
using System.Text;
using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 07, H16 and H17: a co-sponsor's link, and which bookings can still be shared.</summary>
public class ShareTokenTests
{
    [Fact]
    public void ANewTokenIs43Base64UrlCharacters_StoredUnderItsSha256()
    {
        var (token, hash) = ShareToken.New();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token))), hash);
        Assert.Equal(hash, ShareToken.HashOf(token));
    }

    [Fact]
    public void EveryTokenIsNew()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => ShareToken.New().Token).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(100, tokens.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAé")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.")]
    public void AnythingElseIsNoToken(string? token) => Assert.Null(ShareToken.HashOf(token));

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("0123456789-_0123456789-_0123456789-_0123456")]
    public void EveryBase64UrlCharacterIsTaken(string token) => Assert.NotNull(ShareToken.HashOf(token));

    [Fact]
    public void ATokenIsCaseSensitive() =>
        Assert.NotEqual(
            ShareToken.HashOf("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            ShareToken.HashOf("aAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

    [Theory]
    [InlineData(BookingStatus.Requested, 0, true)]
    [InlineData(BookingStatus.Approved, 0, true)]
    [InlineData(BookingStatus.Approved, 30, true)]
    [InlineData(BookingStatus.Approved, -1, false)]
    [InlineData(BookingStatus.Requested, -1, false)]
    [InlineData(BookingStatus.Rejected, 5, false)]
    [InlineData(BookingStatus.Cancelled, 5, false)]
    [InlineData(BookingStatus.Completed, 0, false)]
    [InlineData(BookingStatus.NoShow, 0, false)]
    public void ALiveBookingTodayOrLaterCanBeShared_NothingElse(string status, int daysAhead, bool shareable)
    {
        var today = new DateOnly(2026, 11, 14);

        Assert.Equal(shareable, BookingContributor.IsShareable(status, today.AddDays(daysAhead), today));
    }
}
