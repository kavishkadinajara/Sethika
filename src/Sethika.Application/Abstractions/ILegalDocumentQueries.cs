namespace Sethika.Application.Abstractions;

/// <summary>A legal document as shown to a user before they accept it.</summary>
/// <param name="Code">One of <c>LegalDocumentCodes</c>.</param>
/// <param name="Version">The version, e.g. <c>0.1-draft</c>.</param>
/// <param name="Language">The language this text is in, which may be the fallback.</param>
/// <param name="Title">Shown as the heading.</param>
/// <param name="Content">The full text.</param>
/// <param name="ContentSha256">
/// Sent back on signup to say exactly which text was accepted. See <c>SignUpDonorCommand</c>.
/// </param>
public sealed record CurrentLegalDocument(
    string Code,
    string Version,
    string Language,
    string Title,
    string Content,
    string ContentSha256);

/// <summary>Reads the legal documents a user must accept.</summary>
public interface ILegalDocumentQueries
{
    /// <summary>
    /// The current version of each of <paramref name="codes"/> in <paramref name="language"/>, or in
    /// English where that language has none.
    /// </summary>
    Task<IReadOnlyList<CurrentLegalDocument>> GetCurrentAsync(
        string language,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken = default);
}
