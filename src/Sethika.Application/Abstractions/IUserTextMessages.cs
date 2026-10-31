namespace Sethika.Application.Abstractions;

/// <summary>What became of a text to a user.</summary>
public enum UserTextOutcome
{
    /// <summary>Handed to the SMS sender, or already sent under the same dedup key.</summary>
    Sent,

    /// <summary>The user has no confirmed phone number, or no longer exists. Nothing was sent.</summary>
    NoPhone,
}

/// <summary>Sends a user an SMS from a <c>notify.Templates</c> row, in their own language.</summary>
public interface IUserTextMessages
{
    /// <summary>
    /// Sends <paramref name="templateCode"/> in the user's preferred language, or in English when that
    /// language has no active template, with each <c>{name}</c> in <paramref name="values"/> replaced.
    /// </summary>
    /// <remarks>
    /// Only to a confirmed phone: an unconfirmed number may belong to someone else. A template with
    /// no active English row throws, so the caller's retry and error log say what to seed.
    /// </remarks>
    /// <param name="userId">The recipient.</param>
    /// <param name="templateCode">The template, channel SMS.</param>
    /// <param name="values">The placeholders' values. Never user-typed text.</param>
    /// <param name="dedupKey">A send already recorded under this key is not repeated.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task<UserTextOutcome> SendAsync(
        Guid userId,
        string templateCode,
        IReadOnlyDictionary<string, string> values,
        string dedupKey,
        CancellationToken cancellationToken = default);
}
