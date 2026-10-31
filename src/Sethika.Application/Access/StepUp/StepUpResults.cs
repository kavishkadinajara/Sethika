using System.Text;

namespace Sethika.Application.Access.StepUp;

/// <summary>Why a step-up could not even begin.</summary>
public enum StepUpFailure
{
    /// <summary>The step-up went ahead; see the code outcome.</summary>
    None = 0,

    /// <summary>
    /// The account is suspended, deactivated or deleted. Its access token keeps working until it
    /// expires (plan A2), but it cannot be used to reach anything that needs a step-up.
    /// </summary>
    AccountNotActive = 1,

    /// <summary>The account has no confirmed phone number to send a code to.</summary>
    NoVerifiedPhone = 2,
}

/// <summary>A step-up code check, for the user and the access token presenting it.</summary>
/// <param name="UserId">The authenticated user.</param>
/// <param name="TokenId">The access token's <c>jti</c>; the grant is bound to it.</param>
/// <param name="Code">The code as the user typed it.</param>
/// <param name="IpAddress">For the history row.</param>
/// <param name="UserAgent">For the history row.</param>
public sealed record StepUpVerifyCommand(Guid UserId, string TokenId, string? Code, string? IpAddress, string? UserAgent)
{
    /// <summary>Leaves the code out of <see cref="object.ToString"/>.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserId = ").Append(UserId).Append(", TokenId = ").Append(TokenId);
        return true;
    }
}

/// <summary>The outcome of asking for a step-up code.</summary>
/// <param name="Failure">Why no code was attempted, or <see cref="StepUpFailure.None"/>.</param>
/// <param name="Send">What happened to the code; null unless <paramref name="Failure"/> is None.</param>
public sealed record StepUpRequestResult(StepUpFailure Failure, OtpSendResult? Send)
{
    public static StepUpRequestResult Refused(StepUpFailure failure) => new(failure, null);

    public static StepUpRequestResult Attempted(OtpSendResult send) => new(StepUpFailure.None, send);
}

/// <summary>The outcome of checking a step-up code.</summary>
/// <remarks>
/// Carries the outcome, not the <see cref="OtpVerifyResult"/>: that holds the ticket a verification
/// issues for signup (plan A17), which is no use here and must not reach a response.
/// </remarks>
/// <param name="Failure">Why no code was checked, or <see cref="StepUpFailure.None"/>.</param>
/// <param name="Outcome">The code check; null unless <paramref name="Failure"/> is None.</param>
/// <param name="AttemptsRemaining">After an incorrect code.</param>
public sealed record StepUpVerifyResult(StepUpFailure Failure, OtpVerifyOutcome? Outcome, int AttemptsRemaining)
{
    /// <summary>True when the code was right and the grant stored.</summary>
    public bool Granted => Failure == StepUpFailure.None && Outcome == OtpVerifyOutcome.Verified;

    public static StepUpVerifyResult Refused(StepUpFailure failure) => new(failure, null, 0);

    public static StepUpVerifyResult Checked(OtpVerifyOutcome outcome, int attemptsRemaining = 0) =>
        new(StepUpFailure.None, outcome, attemptsRemaining);
}
