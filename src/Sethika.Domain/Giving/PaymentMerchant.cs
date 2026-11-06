namespace Sethika.Domain.Giving;

/// <summary>
/// An institution's own PayHere merchant (plan 09, M10). Maps <c>giving.PaymentMerchants</c>.
/// </summary>
/// <remarks>
/// Donors' money goes straight to the institution, never through Sethika. The merchant secret is held
/// only as AES-256-GCM ciphertext (M21) and the API never returns it, sealed or open.
/// </remarks>
public class PaymentMerchant
{
    /// <summary>The only provider <c>CK_PayMerchant_Provider</c> allows.</summary>
    public const string PayHere = "PayHere";

    /// <summary><c>MerchantId</c> is <c>VARCHAR(30)</c>.</summary>
    public const int MaxMerchantId = 30;

    /// <summary>Well inside <c>SecretCipher VARBINARY(256)</c>, which holds the text and a 16-byte tag.</summary>
    public const int MaxSecret = 200;

    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public string Provider { get; set; } = PayHere;
    public string MerchantId { get; set; } = null!;

    /// <summary>Names the configured key that sealed the secret, so keys can be rotated (M21).</summary>
    public string SecretKeyId { get; set; } = null!;

    public byte[] SecretNonce { get; set; } = [];

    /// <summary>The ciphertext followed by the 16-byte tag.</summary>
    public byte[] SecretCipher { get; set; } = [];

    public bool IsEnabled { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
