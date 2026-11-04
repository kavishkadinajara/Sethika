namespace Sethika.Application.Abstractions;

/// <summary>
/// Links a person to the donor books that already know their phone, when they sign up or sign in
/// (plan 08, K1, K14, K36).
/// </summary>
/// <remarks>
/// Every institution's donor book is under Row-Level Security and the person's session belongs to
/// none of them, so this runs with the platform's reach, keyed only by the signed-in person's id: the
/// account must have a confirmed phone and not be deleted, and only rows with no account yet are
/// linked, never a second row of one institution (<c>UX_InstDonor_User</c>). The standing days already
/// booked for a row it links become that person's own bookings, so they see them in My bookings.
/// </remarks>
public interface IDonorBookLinks
{
    /// <summary>Links the account <paramref name="userId"/> wherever its confirmed phone is in a donor book; how many rows were linked.</summary>
    Task<int> LinkAsync(Guid userId, CancellationToken cancellationToken = default);
}
