using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// <see cref="IMerchantSecrets"/> with AES-256-GCM (plan 09, M21): a random nonce per value, and the
/// institution's id as associated data, so a ciphertext moved to another institution's row fails its tag.
/// </summary>
/// <remarks>
/// <see cref="Create"/> reads the keys once and refuses a bad configuration, so a host that could not seal
/// or open a secret does not start, rather than failing a donor's payment.
/// </remarks>
public sealed class AesGcmMerchantSecrets : IMerchantSecrets
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary><c>SecretKeyId</c> is <c>VARCHAR(20)</c>.</summary>
    private const int MaxKeyId = 20;

    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _currentKeyId;

    private AesGcmMerchantSecrets(Dictionary<string, byte[]> keys, string currentKeyId)
    {
        _keys = keys;
        _currentKeyId = currentKeyId;
    }

    /// <exception cref="InvalidOperationException">The configuration is missing or wrong, and says how.</exception>
    public static AesGcmMerchantSecrets Create(PaymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.CurrentKeyId) || options.CurrentKeyId.Length > MaxKeyId)
        {
            throw new InvalidOperationException(
                "Payments:CurrentKeyId is not configured (at most 20 characters), so merchant secrets could not be sealed. Run: " +
                "dotnet user-secrets set \"Payments:CurrentKeyId\" \"dev1\" --project src/Sethika.Api");
        }

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (id, encoded) in options.Keys)
        {
            keys[id] = Decode(id, encoded);
        }

        if (!keys.ContainsKey(options.CurrentKeyId))
        {
            throw new InvalidOperationException(
                $"Payments:Keys:{options.CurrentKeyId} is not configured. Run: dotnet user-secrets set " +
                $"\"Payments:Keys:{options.CurrentKeyId}\" \"<32 random bytes, base64>\" --project src/Sethika.Api");
        }

        return new AesGcmMerchantSecrets(keys, options.CurrentKeyId);
    }

    public SealedSecret Seal(int institutionId, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var plain = Encoding.UTF8.GetBytes(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var sealedBytes = new byte[plain.Length + TagBytes];

        using var aes = new AesGcm(_keys[_currentKeyId], TagBytes);
        aes.Encrypt(nonce, plain, sealedBytes.AsSpan(0, plain.Length), sealedBytes.AsSpan(plain.Length), Associated(institutionId));

        return new SealedSecret(_currentKeyId, nonce, sealedBytes);
    }

    public string Open(int institutionId, SealedSecret value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!_keys.TryGetValue(value.KeyId, out var key))
        {
            throw new InvalidOperationException($"Payments:Keys:{value.KeyId} is not configured, but a stored merchant secret was sealed with it.");
        }

        var length = value.Cipher.Length - TagBytes;
        if (length < 0)
        {
            throw new AuthenticationTagMismatchException();
        }

        var plain = new byte[length];
        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(value.Nonce, value.Cipher.AsSpan(0, length), value.Cipher.AsSpan(length), plain, Associated(institutionId));

        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] Associated(int institutionId) => Encoding.UTF8.GetBytes(institutionId.ToString(CultureInfo.InvariantCulture));

    private static byte[] Decode(string id, string encoded)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"Payments:Keys:{id} is not base64.", exception);
        }

        return key.Length == KeyBytes
            ? key
            : throw new InvalidOperationException($"Payments:Keys:{id} must be exactly {KeyBytes} bytes, base64; it is {key.Length}.");
    }
}
