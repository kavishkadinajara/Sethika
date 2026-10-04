namespace Sethika.Application.Access.PasswordReset;

/// <summary>What a reset ticket's store said about a presented ticket.</summary>
/// <param name="Ticket">
/// The account and stamp it was issued for, or null when it is unknown, expired or already spent -
/// one answer for all three.
/// </param>
/// <param name="IsUnavailable">The store could not be reached, so nothing is known either way.</param>
public readonly record struct PasswordResetTicketLookup(PasswordResetTicket? Ticket, bool IsUnavailable)
{
    /// <summary>The ticket is live and was issued for <paramref name="ticket"/>.</summary>
    public static PasswordResetTicketLookup Found(PasswordResetTicket ticket) => new(ticket, false);

    /// <summary>Unknown, expired or spent.</summary>
    public static PasswordResetTicketLookup Invalid { get; } = new(null, false);

    /// <summary>The store is unreachable.</summary>
    public static PasswordResetTicketLookup Unavailable { get; } = new(null, true);
}
