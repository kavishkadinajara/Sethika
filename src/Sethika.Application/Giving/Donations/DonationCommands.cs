using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Donations;

/// <summary>A gift as the record sheet sends it (plan 09, M1, M3).</summary>
/// <param name="Kind"><c>Money</c> or <c>InKind</c>, as stored.</param>
/// <param name="Amount">Money: in LKR, more than 0, two decimals at most.</param>
/// <param name="Method">Money: a <c>PAYMENT_METHOD</c> code staff may choose (<see cref="DonationHandler.CounterMethods"/>).</param>
/// <param name="Items">In kind: what was given.</param>
/// <param name="Quantity">In kind, optional: how many.</param>
/// <param name="EstimatedValue">In kind, optional: what it is worth in LKR.</param>
/// <param name="Date">The day it was given, in Colombo: not in the future, at most a year back.</param>
/// <param name="Donor">A donor book row's <c>PublicId</c>; or none, and <paramref name="DonorName"/> for a walk-in.</param>
/// <param name="DonorName">A walk-in's name, typed once (no donor book row is made).</param>
/// <param name="DonorPhone">A walk-in's phone, optional. Never texted (M5).</param>
/// <param name="Note">Optional, at most 300 characters; on the receipt.</param>
/// <param name="Need">An open need's <c>PublicId</c>, optional. Items then need <paramref name="Quantity"/>, which counts toward it (M15, M29).</param>
public sealed record DonationInput(
    string? Kind,
    decimal? Amount,
    string? Method,
    string? Items,
    decimal? Quantity,
    decimal? EstimatedValue,
    DateOnly? Date,
    Guid? Donor,
    string? DonorName,
    string? DonorPhone,
    string? Note,
    Guid? Need = null);

/// <summary>A void as the void sheet sends it (M6).</summary>
/// <param name="Version">The donation's <c>RowVersion</c> as the list read it.</param>
public sealed record VoidInput(string? Reason, byte[]? Version);

/// <summary>The outcome of recording or voiding a gift.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
public sealed record DonationResult(string? Error, IReadOnlyList<string> Fields, Guid? PublicId, string? ReceiptNo, byte[]? Version)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No donation with that receipt number in the caller's institution.</summary>
    public const string NotFound = "donation_not_found";

    /// <summary>The donor book has no such person in the caller's institution, or they are no longer active.</summary>
    public const string DonorNotFound = "donor_not_found";

    /// <summary>Someone voided it since it was read; read it again.</summary>
    public const string Stale = "stale";

    /// <summary>The caller's institution has no such need.</summary>
    public const string NeedNotFound = NeedErrors.NotFound;

    public bool Succeeded => Error is null;

    internal static DonationResult For(Guid publicId, string receiptNo, byte[] version) => new(null, [], publicId, receiptNo, version);

    internal static DonationResult Failed(string error) => new(error, [], null, null, null);

    internal static DonationResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null, null);
}
