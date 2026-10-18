using Sethika.Application.Access.PasswordReset;

namespace Sethika.Application.Abstractions;

/// <summary>
/// Holds the single-use tickets that carry a verified reset code to the request that sets the new
/// password - the signup ticket pattern of plan A17, bound to an account instead of a number.
/// </summary>
/// <remarks>
/// Only a hash of the ticket is stored, so a Redis dump yields nothing a client could present. Every
/// method fails closed: a store that cannot be reached issues nothing and confirms nothing.
/// </remarks>
public interface IPasswordResetTickets
{
    /// <summary>Stores <paramref name="ticket"/> as proof for <paramref name="issuedFor"/>.</summary>
    /// <returns>False when it could not be stored.</returns>
    Task<bool> StoreAsync(
        string ticket,
        PasswordResetTicket issuedFor,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>Says what <paramref name="ticket"/> was issued for, without spending it.</summary>
    Task<PasswordResetTicketLookup> FindAsync(string ticket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends <paramref name="ticket"/> and says what it was issued for. Of any number of concurrent
    /// calls with one ticket, exactly one finds it.
    /// </summary>
    Task<PasswordResetTicketLookup> RedeemAsync(string ticket, CancellationToken cancellationToken = default);
}
