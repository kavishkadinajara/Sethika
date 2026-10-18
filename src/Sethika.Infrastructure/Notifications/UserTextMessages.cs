using Dapper;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Looks up a user's confirmed phone and language and the template in that language, fills in the
/// placeholders and hands the text to <see cref="ISmsSender"/>.
/// </summary>
/// <remarks>
/// The template is read on every send, as <c>OtpService</c> reads its own: a corrected template goes
/// out from the next message. <c>auth.Users.PhoneNumber</c> is masked, so the login this runs under
/// needs <c>UNMASK</c> on it before deployment (plan 02, B12).
/// </remarks>
public sealed partial class UserTextMessages(
    IDbConnectionFactory connections,
    ISmsSender sms,
    ILogger<UserTextMessages> logger) : IUserTextMessages
{
    private const string FallbackLanguage = "en";

    private const string Sql =
        """
        SELECT u.PhoneNumber       AS PhoneNumber,
               u.PreferredLanguage AS Language
        FROM auth.Users AS u
        WHERE u.Id = @userId AND u.IsDeleted = 0 AND u.PhoneConfirmed = 1 AND u.PhoneNumber IS NOT NULL;

        SELECT t.Language AS Language,
               t.Body     AS Body
        FROM notify.Templates AS t
        WHERE t.Code = @code AND t.Channel = 'SMS' AND t.IsActive = 1;
        """;

    public async Task<UserTextOutcome> SendAsync(
        Guid userId,
        string templateCode,
        IReadOnlyDictionary<string, string> values,
        string dedupKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);

        Recipient? recipient;
        List<Template> templates;
        await using (var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                Sql,
                new { userId, code = templateCode },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            recipient = await grid.ReadSingleOrDefaultAsync<Recipient>().ConfigureAwait(false);
            templates = (await grid.ReadAsync<Template>().ConfigureAwait(false)).AsList();
        }

        if (recipient is null)
        {
            LogNoPhone(logger, templateCode, userId);
            return UserTextOutcome.NoPhone;
        }

        var body = templates.Find(t => t.Language == recipient.Language)?.Body;
        if (body is null)
        {
            body = templates.Find(t => t.Language == FallbackLanguage)?.Body
                   ?? throw new InvalidOperationException(
                       $"No active SMS template {templateCode} in '{recipient.Language}' or in English. Seed notify.Templates.");
            LogFellBack(logger, templateCode, recipient.Language);
        }

        foreach (var (name, value) in values)
        {
            body = body.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        await sms.SendAsync(recipient.PhoneNumber, body, dedupKey, cancellationToken).ConfigureAwait(false);
        return UserTextOutcome.Sent;
    }

    private sealed record Recipient(string PhoneNumber, string Language);

    private sealed record Template(string Language, string Body);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No SMS {TemplateCode} for user {UserId}: no confirmed phone number.")]
    private static partial void LogNoPhone(ILogger logger, string templateCode, Guid userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No active SMS template {TemplateCode} in '{Language}'; sent the English one. Seed notify.Templates.")]
    private static partial void LogFellBack(ILogger logger, string templateCode, string language);
}
