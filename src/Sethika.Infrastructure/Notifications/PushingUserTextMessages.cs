using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using Sethika.Domain.Notifications;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Sends the SMS, then a push to each of the user's phones when a <c>Channel = 'Push'</c> template of
/// that code exists (plan F, F26). Which events are pushed is therefore seed data, and the reminder
/// job is covered as well as the outbox handlers.
/// </summary>
/// <remarks>
/// <para>
/// SMS is the guarantee and push is in addition (F5). A failed SMS still throws, so the caller's retry
/// comes back through here, and no push has gone yet. Nothing about a push ever throws (F29): it is
/// recorded <c>Failed</c>, logged and not tried again, and the SMS and the outbox message stand.
/// </para>
/// <para>
/// One <c>notify.Notifications</c> row per device, claimed before the send exactly as
/// <see cref="SmsSenderBase"/> claims its own: <c>UX_Notifications_Dedup</c> refusing the insert is the
/// whole guarantee of "never twice". The key has its own prefix because the SMS holds the plain key,
/// and the device's id so that a second phone is not blocked by the first (F28).
/// </para>
/// <para>
/// <see cref="SendToPhoneAsync"/> is not pushed: a roster number has no account, so no device.
/// </para>
/// </remarks>
public sealed partial class PushingUserTextMessages(
    IUserTextMessages texts,
    IDbConnectionFactory connections,
    IPushGateway gateway,
    ILogger<PushingUserTextMessages> logger) : IUserTextMessages
{
    private const string FallbackLanguage = "en";

    private const string TargetSql =
        """
        SELECT t.Id       AS Id,
               t.Language AS Language,
               t.Subject  AS Subject,
               t.Body     AS Body
        FROM notify.Templates AS t
        WHERE t.Code = @code AND t.Channel = 'Push' AND t.IsActive = 1;

        SELECT u.PreferredLanguage AS Language
        FROM auth.Users AS u
        WHERE u.Id = @userId AND u.IsDeleted = 0;

        SELECT d.Id            AS Id,
               d.ExpoPushToken AS Token
        FROM notify.MobileDevices AS d
        WHERE d.UserId = @userId AND d.DisabledAt IS NULL;
        """;

    private const string ClaimSql =
        """
        INSERT INTO notify.Notifications
            (UserId, TemplateId, Channel, Recipient, Subject, Body, DedupKey, Status, ScheduledAt, Attempts)
        VALUES
            (@userId, @templateId, 'Push', @token, @title, @body, @dedupKey, 'Queued', SYSUTCDATETIME(), 1);

        SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
        """;

    private const string SentSql =
        """
        UPDATE notify.Notifications
        SET Status = 'Sent', SentAt = SYSUTCDATETIME(), ProviderMessageId = @ticketId
        WHERE Id = @id;
        """;

    private const string FailedSql =
        "UPDATE notify.Notifications SET Status = 'Failed', ErrorMessage = LEFT(@error, 500) WHERE Id = @id;";

    public async Task<UserTextOutcome> SendAsync(
        Guid userId,
        string templateCode,
        IReadOnlyDictionary<string, string> values,
        string dedupKey,
        CancellationToken cancellationToken = default)
    {
        var outcome = await texts.SendAsync(userId, templateCode, values, dedupKey, cancellationToken).ConfigureAwait(false);

        try
        {
            await PushAsync(userId, templateCode, values, dedupKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotPushed(logger, ex, templateCode, userId);
        }

        return outcome;
    }

    public Task SendToPhoneAsync(
        string phoneNumber,
        string language,
        string templateCode,
        IReadOnlyDictionary<string, string> values,
        string dedupKey,
        CancellationToken cancellationToken = default) =>
        texts.SendToPhoneAsync(phoneNumber, language, templateCode, values, dedupKey, cancellationToken);

    private async Task PushAsync(
        Guid userId,
        string templateCode,
        IReadOnlyDictionary<string, string> values,
        string dedupKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        List<Template> templates;
        string? language;
        List<Device> devices;
        using (var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            TargetSql,
            new { code = templateCode, userId },
            cancellationToken: cancellationToken)).ConfigureAwait(false))
        {
            templates = (await grid.ReadAsync<Template>().ConfigureAwait(false)).AsList();
            language = await grid.ReadSingleOrDefaultAsync<string>().ConfigureAwait(false);
            devices = (await grid.ReadAsync<Device>().ConfigureAwait(false)).AsList();
        }

        if (language is null || devices.Count == 0)
        {
            return;
        }

        var template = templates.Find(t => t.Language == language) ?? templates.Find(t => t.Language == FallbackLanguage);
        if (template is null)
        {
            return;
        }

        var title = template.Subject is null ? null : TemplateText.Fill(template.Subject, values);
        var body = TemplateText.Fill(template.Body, values);

        foreach (var device in devices)
        {
            await PushToAsync(connection, userId, device, template.Id, new PushMessage(device.Token, title, body, templateCode), dedupKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task PushToAsync(
        DbConnection connection,
        Guid userId,
        Device device,
        short templateId,
        PushMessage message,
        string dedupKey,
        CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(connection, userId, device, templateId, message, dedupKey, cancellationToken).ConfigureAwait(false);
        if (claimed is not { } id)
        {
            return;
        }

        PushAnswer answer;
        try
        {
            answer = await gateway.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Whatever stopped the send, the row must not stay Queued. On shutdown too, hence no token.
            await RecordFailureAsync(connection, id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            LogFailed(logger, ex, message.TemplateCode, device.Id);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (answer.Error is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                SentSql,
                new { id, ticketId = answer.TicketId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await RecordFailureAsync(connection, id, answer.Error, cancellationToken).ConfigureAwait(false);
        LogRefused(logger, message.TemplateCode, device.Id, answer.Error);

        if (answer.DeviceIsGone)
        {
            await PushDevices.DisableGoneAsync(connection, message.Token, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Inserts the queued row, or returns null when this device already has this push.</summary>
    private static async Task<long?> ClaimAsync(
        DbConnection connection,
        Guid userId,
        Device device,
        short templateId,
        PushMessage message,
        string dedupKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connection.QuerySingleAsync<long>(new CommandDefinition(
                ClaimSql,
                new
                {
                    userId,
                    templateId,
                    token = message.Token,
                    title = message.Title,
                    body = message.Body,
                    dedupKey = $"push:{dedupKey}:{device.Id}",
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return null;
        }
    }

    private static Task<int> RecordFailureAsync(DbConnection connection, long id, string error, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(FailedSql, new { id, error }, cancellationToken: cancellationToken));

    private sealed record Template(short Id, string Language, string? Subject, string Body);

    private sealed record Device(int Id, string Token);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Push {TemplateCode} for user {UserId} was not sent; the SMS stands.")]
    private static partial void LogNotPushed(ILogger logger, Exception exception, string templateCode, Guid userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Push {TemplateCode} to device {DeviceId} failed; it will not be retried.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string templateCode, int deviceId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Push {TemplateCode} to device {DeviceId} was refused: {Reason}.")]
    private static partial void LogRefused(ILogger logger, string templateCode, int deviceId, string reason);
}

/// <summary>What a send and a receipt both do to a device.</summary>
internal static class PushDevices
{
    private const string DisableGoneSql =
        """
        UPDATE notify.MobileDevices
        SET DisabledAt = SYSUTCDATETIME(), DisabledReason = @reason
        WHERE ExpoPushToken = @token AND DisabledAt IS NULL;
        """;

    /// <summary>Stops sending to a token Expo says no longer reaches a phone. Registering it again undoes this.</summary>
    public static Task DisableGoneAsync(DbConnection connection, string token, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            DisableGoneSql,
            new { token = new DbString { Value = token, IsAnsi = true, Length = 200 }, reason = MobileDevice.DisabledReasons.DeviceNotRegistered },
            cancellationToken: cancellationToken));
}
