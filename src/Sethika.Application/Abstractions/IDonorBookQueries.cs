namespace Sethika.Application.Abstractions;

/// <summary>A person in the donor book, as its screen shows and edits them (plan 08, K1).</summary>
/// <param name="HasAccount">Whether the person is linked to a Sethika account, so sees their standing days in the app.</param>
/// <param name="TextConsentAt">When staff recorded that the person agreed to receive texts (UTC); null if not (Q2).</param>
/// <param name="ActiveSponsorships">Their standing sponsorships still running.</param>
/// <param name="Version">The <c>RowVersion</c> an edit sends back.</param>
public sealed record DonorView(
    Guid PublicId,
    string FullName,
    string Phone,
    string? Notes,
    string Language,
    bool HasAccount,
    DateTime? TextConsentAt,
    bool IsActive,
    int ActiveSponsorships,
    byte[] Version);

/// <summary>Reads for the institution's donor book (plan 08, K1).</summary>
public interface IDonorBookQueries
{
    /// <summary>The institution's donor book, active first, by name.</summary>
    Task<IReadOnlyList<DonorView>> ListDonorsAsync(int institutionId, CancellationToken cancellationToken = default);
}
