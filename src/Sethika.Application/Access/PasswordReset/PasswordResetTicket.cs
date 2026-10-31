namespace Sethika.Application.Access.PasswordReset;

/// <summary>What a reset ticket was issued for: one account, as it stood when its code was verified.</summary>
/// <param name="UserId">The account whose password the ticket may set.</param>
/// <param name="SecurityStamp">
/// The account's stamp at verification. Anything that rotates the stamp in between - a logout
/// everywhere, another reset - makes the ticket worthless, so a ticket cannot outlive a decision the
/// account holder took after it was issued.
/// </param>
public sealed record PasswordResetTicket(Guid UserId, string SecurityStamp);
