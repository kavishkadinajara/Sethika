using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Sethika.Application.Access;
using Sethika.Infrastructure.Security;
using Xunit;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// What goes into each token. Whether the API then accepts it is <see cref="JwtTokenServiceEndpointTests"/>.
/// </summary>
public sealed class JwtTokenServiceTests
{
    private const string SigningKey = "jwt-token-service-test-key-at-least-32-bytes!!";

    /// <summary>
    /// Deliberately not on a whole second, so the truncation to JWT's one-second resolution is
    /// visible in the assertions rather than accidentally satisfied.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, 750, TimeSpan.Zero);

    private static readonly DateTimeOffset NowInWholeSeconds = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    private static readonly TokenSubject Subject =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 42, IsPlatform: false);

    internal static JwtTokenService CreateService(string signingKey = SigningKey, TimeProvider? time = null) =>
        new(Options.Create(new JwtOptions { SigningKey = signingKey }), time ?? new FixedTimeProvider(Now));

    private static JsonWebToken Issue(TokenSubject subject) => new(CreateService().Issue(subject).Access.Token);

    [Fact]
    public void PutsTheUserInSubAndInTheClaimTheApiReads()
    {
        var jwt = Issue(Subject);

        Assert.Equal(Subject.UserId.ToString(), jwt.Subject);
        Assert.Equal(Subject.UserId.ToString(), jwt.GetPayloadValue<string>(SethikaClaimTypes.UserId));
    }

    [Fact]
    public void CarriesTheInstitutionTheSessionActsFor()
    {
        var jwt = Issue(Subject);

        Assert.Equal(42, jwt.GetPayloadValue<int>(SethikaClaimTypes.InstitutionId));
    }

    [Fact]
    public void OmitsTheInstitutionClaimWhenTheSessionActsForNone()
    {
        // Absent, not 0 and not null. A missing claim is what the Api reads as "no tenant"; a 0
        // would be parsed as a tenant id and stamped into the RLS session context.
        var jwt = Issue(Subject with { InstitutionId = null });

        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == SethikaClaimTypes.InstitutionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CarriesThePlatformFlag(bool isPlatform)
    {
        var jwt = Issue(Subject with { IsPlatform = isPlatform });

        Assert.Equal(isPlatform, jwt.GetPayloadValue<bool>(SethikaClaimTypes.IsPlatform));
    }

    [Fact]
    public void IsIssuedByAndForSethikaAndSignedWithHs256()
    {
        var jwt = Issue(Subject);

        Assert.Equal(SethikaJwt.Issuer, jwt.Issuer);
        Assert.Equal(SethikaJwt.Audience, Assert.Single(jwt.Audiences));
        Assert.Equal("HS256", jwt.Alg);
    }

    [Fact]
    public void ReportsTheJwtIdItSigned_AndNeverReusesOne()
    {
        // The step-up grant and the refresh row are both keyed by this id, so the value handed back
        // must be the one inside the token, and two tokens must never share one.
        var service = CreateService();

        var first = service.Issue(Subject).Access;
        var second = service.Issue(Subject).Access;

        Assert.Equal(first.JwtId, new JsonWebToken(first.Token).Id);
        Assert.NotEqual(first.JwtId, second.JwtId);
    }

    [Fact]
    public void AccessTokenLastsFifteenMinutes_AndExpiresAtIsExactlyItsExp()
    {
        var access = CreateService().Issue(Subject).Access;
        var jwt = new JsonWebToken(access.Token);

        Assert.Equal(NowInWholeSeconds.UtcDateTime, jwt.IssuedAt);
        Assert.Equal(NowInWholeSeconds.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(NowInWholeSeconds.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(jwt.ValidTo, access.ExpiresAt.UtcDateTime);
    }

    [Fact]
    public void RefreshTokenIs32RandomBytesInUnpaddedBase64Url()
    {
        var service = CreateService();

        var first = service.Issue(Subject).Refresh.Token;
        var second = service.Issue(Subject).Refresh.Token;

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.Equal(32, Base64Url.DecodeFromChars(first).Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void StoresOnlyTheSha256OfTheRefreshToken_AndLooksItUpTheSameWay()
    {
        var service = CreateService();
        var refresh = service.Issue(Subject).Refresh;

        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refresh.Token)));

        Assert.Equal(expected, refresh.TokenHash);

        // The refresh endpoint finds the row by hashing what the cookie presents. If that path hashed
        // differently from the one that stored it, no refresh token would ever be found.
        Assert.Equal(refresh.TokenHash, service.HashRefreshToken(refresh.Token));
    }

    [Fact]
    public void RefreshTokenLastsFourteenDays()
    {
        var refresh = CreateService().Issue(Subject).Refresh;

        Assert.Equal(NowInWholeSeconds.AddDays(14), refresh.ExpiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890")] // 31 bytes: one short of 256 bits
    public void RefusesASigningKeyShorterThan256Bits(string signingKey)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateService(signingKey));

        Assert.Contains("Jwt:SigningKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsASigningKeyOfExactly256Bits()
    {
        var service = CreateService("01234567890123456789012345678901");

        Assert.NotEmpty(service.Issue(Subject).Access.Token);
    }

    [Fact]
    public void RefusesToIssueForAnEmptyUserId()
    {
        Assert.Throws<ArgumentException>(() => CreateService().Issue(Subject with { UserId = Guid.Empty }));
    }
}
