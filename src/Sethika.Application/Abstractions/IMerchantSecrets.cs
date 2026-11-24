namespace Sethika.Application.Abstractions;

/// <summary>A merchant secret as it is stored (plan 09, M21).</summary>
/// <param name="KeyId">The configured key that sealed it.</param>
/// <param name="Nonce">12 random bytes, new for every value.</param>
/// <param name="Cipher">The ciphertext followed by the 16-byte tag.</param>
public sealed record SealedSecret(string KeyId, byte[] Nonce, byte[] Cipher);

/// <summary>
/// Seals and opens institutions' merchant secrets (M21). A value sealed for one institution does not open
/// for another, so a ciphertext copied between rows is useless.
/// </summary>
public interface IMerchantSecrets
{
    /// <summary>Seals <paramref name="secret"/> for <paramref name="institutionId"/> with the current key.</summary>
    SealedSecret Seal(int institutionId, string secret);

    /// <summary>Opens a stored value with the key it names.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with, or belongs to another institution.</exception>
    /// <exception cref="InvalidOperationException">The key it names is no longer configured.</exception>
    string Open(int institutionId, SealedSecret value);
}
