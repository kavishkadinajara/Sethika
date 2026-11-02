namespace Sethika.Domain.Bookings;

/// <summary>
/// A person in an institution's donor book (plan 08, K1). Maps <c>org.InstitutionDonors</c>.
/// </summary>
/// <remarks>
/// A person record, not an account: many of an institution's regular donors never use the app, and
/// staff answer for them. A person whose phone is a Sethika account's confirmed phone is linked by
/// <see cref="UserId"/>. One row per phone per institution.
/// </remarks>
public class InstitutionDonor
{
    public int Id { get; set; }

    /// <summary>The donor's address in APIs (v7, set in C#).</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public Guid? UserId { get; set; }
    public string FullName { get; set; } = null!;

    /// <summary>E.164; masked in the database.</summary>
    public string Phone { get; set; } = null!;

    public string? Notes { get; set; }

    /// <summary>The language of texts to a donor without an account: en, si or ta (K10).</summary>
    public string Language { get; set; } = "si";

    /// <summary>When staff recorded that the person agreed to receive texts (Q2); null if they did not.</summary>
    public DateTime? TextConsentAt { get; set; }

    /// <summary>Who recorded it; set with <see cref="TextConsentAt"/>.</summary>
    public Guid? TextConsentBy { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Records whether the person agreed to receive texts. Ticking it again keeps who first recorded it
    /// and when; unticking it forgets both.
    /// </summary>
    public void SetTextConsent(bool agreed, Guid by, DateTime utcNow)
    {
        if (!agreed)
        {
            TextConsentAt = null;
            TextConsentBy = null;
        }
        else if (TextConsentAt is null)
        {
            TextConsentAt = utcNow;
            TextConsentBy = by;
        }
    }
}
