using Sethika.Application.Access;

namespace Sethika.Application.Abstractions;

/// <summary>
/// Issues and checks short-lived one-time codes sent by SMS.
/// </summary>
/// <remarks>
/// Codes are never persisted to SQL. They live in Redis with a short expiry, which keeps a
/// short-lived secret out of the database, needs no cleanup job, and leaves nothing to retain
/// under PDPA. The durable record of a verification is <c>auth.Users.PhoneConfirmed</c> and
/// <c>auth.LoginHistory</c>.
///
/// The consequence, accepted deliberately: if Redis is unreachable, no code can be issued or
/// checked, so signup and step-up stop working. Password login is unaffected. This is the
/// opposite of the permission cache, which degrades to a SQL read - there is no safe fallback
/// for a one-time code, because inventing one would mean accepting a code nobody issued.
/// </remarks>
public interface IOtpService
{
    /// <summary>
    /// Generates a code for <paramref name="phoneNumber"/>, stores only its hash, and sends it.
    /// </summary>
    /// <param name="phoneNumber">The recipient in E.164 form.</param>
    /// <param name="purpose">Which flow the code is valid for. A code is not valid for any other.</param>
    /// <param name="language">Which language to send the message in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<OtpSendResult> SendAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Does everything <see cref="SendAsync"/> does - the cooldown, the per-number send count, a
    /// stored code - except send it.
    /// </summary>
    /// <remarks>
    /// For flows that must not say whether a number has an account, such as a password reset. A
    /// number that gets no SMS still has to hit the same cooldown, reach the same 429 on the same
    /// request, and answer a guessed code the same way, or those differences become the answer.
    /// The code is random and never leaves the server, so it cannot be presented.
    /// </remarks>
    /// <param name="phoneNumber">The number in E.164 form.</param>
    /// <param name="purpose">The flow the decoy stands in for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<OtpSendResult> SendDecoyAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks <paramref name="code"/> against the outstanding code for this number and purpose,
    /// consuming it on success so it cannot be presented twice, and issuing a ticket that proves the
    /// verification to the step that follows.
    /// </summary>
    /// <param name="phoneNumber">The number the code was sent to, in E.164 form.</param>
    /// <param name="purpose">The flow the code was issued for.</param>
    /// <param name="code">The code as the user typed it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<OtpVerifyResult> VerifyAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends the ticket <see cref="VerifyAsync"/> issued for this number and purpose. It succeeds
    /// once; every later attempt, and any attempt for another number or purpose, is refused.
    /// </summary>
    /// <param name="phoneNumber">The number that was verified, in E.164 form.</param>
    /// <param name="purpose">The flow the code was verified for.</param>
    /// <param name="ticket">The ticket as the client presented it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<OtpTicketOutcome> RedeemTicketAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string ticket,
        CancellationToken cancellationToken = default);
}
