using Sethika.Application.Giving.Needs;

namespace Sethika.Application.Giving.Payments;

/// <summary>The merchant settings as the screen sends them (plan 09, M10).</summary>
/// <param name="MerchantId">PayHere's merchant id.</param>
/// <param name="Secret">The merchant secret. Left out, the stored one stays, unless <paramref name="MerchantId"/> changed.</param>
/// <param name="Enabled">Whether donors may give online.</param>
/// <param name="Version">The row's <c>RowVersion</c> as the screen read it; none for the first save.</param>
public sealed record MerchantInput(string? MerchantId, string? Secret, bool Enabled, byte[]? Version);

/// <summary>The outcome of saving the merchant settings.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix.</param>
/// <param name="Version">The saved row's new <c>RowVersion</c>.</param>
public sealed record MerchantResult(string? Error, IReadOnlyList<string> Fields, byte[]? Version)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>Someone saved the settings since they were read; read them again.</summary>
    public const string Stale = "stale";

    public bool Succeeded => Error is null;

    internal static MerchantResult For(byte[] version) => new(null, [], version);

    internal static MerchantResult Failed(string error) => new(error, [], null);

    internal static MerchantResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null);
}

/// <summary>A gift as the give sheet sends it (M8).</summary>
/// <param name="Amount">In LKR, from 100 to 1,000,000, in whole cents.</param>
/// <param name="Need">An open need of the institution the gift is for, optional. Money never counts toward it (M29).</param>
/// <param name="Email">PayHere requires it. Passed to the checkout form, never stored (M33).</param>
/// <param name="Address">PayHere requires it. Passed to the checkout form, never stored (M33).</param>
/// <param name="City">PayHere requires it. Passed to the checkout form, never stored (M33).</param>
public sealed record GiveInput(decimal? Amount, Guid? Need, string? Email, string? Address, string? City);

/// <summary>What the browser posts to PayHere (M8).</summary>
/// <param name="OrderId">The order this form pays, and the return page's address.</param>
/// <param name="Action">PayHere's checkout address.</param>
/// <param name="Fields">The form's fields, by PayHere's names, with the <c>hash</c>.</param>
public sealed record CheckoutForm(Guid OrderId, string Action, IReadOnlyDictionary<string, string> Fields);

/// <summary>The outcome of starting a gift.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix.</param>
public sealed record GiveResult(string? Error, IReadOnlyList<string> Fields, CheckoutForm? Form)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>The account has no confirmed phone (M8).</summary>
    public const string PhoneRequired = NeedResult.PhoneRequired;

    /// <summary>No such institution taking gifts.</summary>
    public const string InstitutionNotFound = "institution_not_found";

    /// <summary>The institution has no enabled merchant (M10).</summary>
    public const string OnlineGivingOff = "online_giving_off";

    public bool Succeeded => Error is null;

    internal static GiveResult For(CheckoutForm form) => new(null, [], form);

    internal static GiveResult Failed(string error) => new(error, [], null);

    internal static GiveResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null);
}

/// <summary>PayHere's notification, field by field, exactly as it was posted (M9).</summary>
public sealed record PayHereNotification(
    string? MerchantId,
    string? OrderId,
    string? PaymentId,
    string? Amount,
    string? Currency,
    string? StatusCode,
    string? Signature);
