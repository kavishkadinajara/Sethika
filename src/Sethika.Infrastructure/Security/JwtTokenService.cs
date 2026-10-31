using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Issues a signed JWT access token and an opaque refresh token.
/// </summary>
/// <remarks>
/// The access token is a JWT because the API must check it on every request without a database
/// read. It carries exactly the three values that stamp the RLS session context - user,
/// institution, platform flag - plus <c>sub</c> and <c>jti</c>. Nothing else goes in: every claim
/// is readable by anyone holding the token and stays true, whatever changes, until it expires.
/// Permissions in particular are not claims; they come from the cache, keyed by version, so a
/// revoked grant stops working at once rather than when the token runs out.
///
/// The refresh token is deliberately not a JWT. It is 32 bytes from a cryptographic RNG and means
/// nothing by itself; only its SHA-256 is stored, so a leaked copy of <c>auth.RefreshTokens</c>
/// yields no usable session. The hash is unsalted and fast on purpose: salting and slow hashing
/// exist to protect low-entropy secrets such as passwords from guessing, and 256 random bits cannot
/// be guessed. A deterministic hash is also what lets the presented token be found by an indexed
/// equality lookup.
/// </remarks>
public sealed class JwtTokenService : ITokenService
{
    private const int RefreshTokenBytes = 32;

    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the service, refusing a signing key that is too short to be safe.</summary>
    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        _timeProvider = timeProvider;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.AccessTokenLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.RefreshTokenLifetime, TimeSpan.Zero);

        _signingCredentials = new SigningCredentials(
            SethikaJwt.CreateSigningKey(_options.SigningKey),
            SethikaJwt.SigningAlgorithm);
    }

    public IssuedTokens Issue(TokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (subject.UserId == Guid.Empty)
        {
            throw new ArgumentException("A token cannot be issued to an empty user id.", nameof(subject));
        }

        // JWT times are whole seconds. Truncating first makes the ExpiresAt handed back exactly
        // the exp inside the token, rather than up to a second later than the API will accept it.
        var now = DateTimeOffset.FromUnixTimeSeconds(_timeProvider.GetUtcNow().ToUnixTimeSeconds());

        return new IssuedTokens(CreateAccessToken(subject, now), CreateRefreshToken(now));
    }

    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        // The string as presented is hashed, not the bytes it decodes to. Base64 decoders tolerate
        // variations - padding, trailing bits - so hashing decoded bytes would let more than one
        // string match a stored token. Only the exact string that was issued should.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    private IssuedAccessToken CreateAccessToken(TokenSubject subject, DateTimeOffset now)
    {
        var jwtId = Guid.NewGuid().ToString("N");
        var userId = subject.UserId.ToString();
        var expiresAt = now + _options.AccessTokenLifetime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId,
            [JwtRegisteredClaimNames.Jti] = jwtId,
            [SethikaClaimTypes.UserId] = userId,
            [SethikaClaimTypes.IsPlatform] = subject.IsPlatform,
        };

        // Omitted rather than written as null or 0 when the session acts for no institution. A
        // missing claim is what the Api reads as "no tenant", and 0 would be read as a tenant id.
        if (subject.InstitutionId is { } institutionId)
        {
            claims[SethikaClaimTypes.InstitutionId] = institutionId;
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = SethikaJwt.Issuer,
            Audience = SethikaJwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials,
        });

        return new IssuedAccessToken(token, jwtId, expiresAt);
    }

    private IssuedRefreshToken CreateRefreshToken(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[RefreshTokenBytes];
        RandomNumberGenerator.Fill(bytes);

        // Base64url, unpadded: the token travels in a cookie, where '+', '/' and '=' would each
        // need escaping and each escape is a place for a client and server to disagree.
        var token = Base64Url.EncodeToString(bytes);

        return new IssuedRefreshToken(token, HashRefreshToken(token), now + _options.RefreshTokenLifetime);
    }
}
