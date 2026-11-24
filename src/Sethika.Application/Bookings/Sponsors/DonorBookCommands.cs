namespace Sethika.Application.Bookings.Sponsors;

/// <summary>A person in the donor book, as the donor form sends them (plan 08, K1).</summary>
/// <param name="Phone">As typed; the person's texts go here, and an account with it as its confirmed phone is linked.</param>
/// <param name="Language">Of texts to a donor without an account: <c>en</c>, <c>si</c> (the default) or <c>ta</c>.</param>
/// <param name="TextConsent">"This person agreed to receive texts" (Q2). Unticked, a donor without an account is never texted.</param>
/// <param name="Version">An edit's: the donor's <c>RowVersion</c> as the editor read it. A new donor has none.</param>
public sealed record DonorInput(
    string? FullName,
    string? Phone,
    string? Notes,
    string? Language,
    bool? TextConsent,
    byte[]? Version = null);

/// <summary>The outcome of adding or changing a person in the donor book.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="PublicId">The donor added or changed.</param>
/// <param name="Version">Its <c>RowVersion</c> after the change, for the next edit.</param>
public sealed record DonorChangeResult(string? Error, IReadOnlyList<string> Fields, Guid? PublicId, byte[]? Version)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No donor with that id in the caller's institution.</summary>
    public const string DonorNotFound = "donor_not_found";

    /// <summary>The donor book already has someone with that phone (<c>UQ_InstDonor_Phone</c>).</summary>
    public const string PhoneTaken = "donor_phone_taken";

    /// <summary>
    /// The account with that phone is already linked to another person in the donor book
    /// (<c>UX_InstDonor_User</c>): that person's phone was the account's before it changed.
    /// </summary>
    public const string AccountTaken = "donor_account_taken";

    /// <summary>Someone changed it since it was read; read it again.</summary>
    public const string Stale = "stale";

    public bool Succeeded => Error is null;

    internal static DonorChangeResult For(Guid publicId, byte[] version) => new(null, [], publicId, version);

    internal static DonorChangeResult Failed(string error) => new(error, [], null, null);

    internal static DonorChangeResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null);
}
