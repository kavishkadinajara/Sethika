namespace Sethika.Domain.Core;

/// <summary>
/// One version of a legal text in one language. Maps <c>core.LegalDocuments</c>, read-only here.
/// </summary>
/// <remarks>
/// <c>Content</c> is deliberately not mapped: commands only need to know which version a user
/// accepted, and <see cref="ContentSha256"/> identifies the exact text without loading it.
/// Versions are reference data seeded by the SQL project and never written through EF.
/// </remarks>
public class LegalDocument
{
    public short Id { get; set; }
    public string Code { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string Language { get; set; } = null!;
    public string Title { get; set; } = null!;

    /// <summary>SHA-256 of the content as stored (UTF-16LE), lowercase hex.</summary>
    public string ContentSha256 { get; set; } = null!;

    public DateTime EffectiveFrom { get; set; }
    public bool IsCurrent { get; set; }
}
