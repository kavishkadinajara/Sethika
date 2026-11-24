using System.Globalization;
using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>
/// The standing sponsorships' texts (plan 08, K10), written to the outbox in the transaction of the
/// change they tell of, and sent by <see cref="StandingSmsHandler"/>.
/// </summary>
/// <remarks>
/// Each text has its own dedup key, made here, so a change written twice - or a message run twice -
/// texts nobody twice. Like every Sethika text, none carries anything typed: not a reason, not the
/// slot's name.
/// </remarks>
public static class StandingTexts
{
    /// <summary>A sponsorship's text is due: its payload names the template, the sponsorship and the days.</summary>
    public const string Event = "StandingTextDue";

    /// <summary>A day request was answered: its payload names the request and whether it was given.</summary>
    public const string AnsweredEvent = "DayRequestAnswered";

    public const string Created = "STANDING_CREATED";
    public const string Skipped = "STANDING_SKIPPED";
    public const string Released = "STANDING_RELEASED";
    public const string Blocked = "STANDING_BLOCKED";
    public const string Swapped = "STANDING_SWAPPED";
    public const string SwappedOn = "STANDING_SWAPPED_ON";
    public const string RequestGiven = "DAY_REQUEST_GIVEN";
    public const string RequestDeclined = "DAY_REQUEST_DECLINED";

    /// <summary>
    /// <paramref name="template"/> to <paramref name="sponsorship"/>'s donor about <paramref name="date"/>:
    /// the day it begins with, the day let go, or a swap's new day.
    /// </summary>
    /// <param name="last">A block's last day let go, from <paramref name="date"/>.</param>
    /// <param name="other">A swap's old day.</param>
    /// <param name="key">What makes the change one of a kind, after the template and the sponsorship.</param>
    public static OutboxMessage Message(
        string template,
        Guid sponsorship,
        DateOnly date,
        string key,
        DateTime now,
        DateOnly? last = null,
        DateOnly? other = null) =>
        new()
        {
            EventType = Event,
            AggregateType = nameof(StandingSponsorship),
            AggregateId = sponsorship.ToString(),
            Payload = JsonSerializer.Serialize(new
            {
                template,
                sponsorship,
                date = Iso(date),
                last = last is { } l ? Iso(l) : null,
                other = other is { } o ? Iso(o) : null,
                dedupKey = $"{template}:{sponsorship}:{key}",
            }),
            OccurredAt = now,
        };

    /// <summary>The answer to day request <paramref name="requestNo"/>, to the donor who asked.</summary>
    public static OutboxMessage Answered(string requestNo, bool given, DateTime now) =>
        new()
        {
            EventType = AnsweredEvent,
            AggregateType = nameof(DayRequest),
            AggregateId = requestNo,
            Payload = JsonSerializer.Serialize(new { requestNo, given }),
            OccurredAt = now,
        };

    internal static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Sends <see cref="StandingTexts"/>' messages. A sponsor with a linked account is texted as any account
/// is; one without is texted at the phone the institution gave only if staff ticked the consent box
/// (Q2), and otherwise not at all - the institution tells them itself.
/// </summary>
public sealed class StandingSmsHandler(IBookingNotificationQueries queries, IUserTextMessages texts) : IOutboxHandler
{
    private static readonly HashSet<string> StandingTemplates = new(StringComparer.Ordinal)
    {
        StandingTexts.Created,
        StandingTexts.Skipped,
        StandingTexts.Released,
        StandingTexts.Blocked,
        StandingTexts.Swapped,
        StandingTexts.SwappedOn,
    };

    public IReadOnlyCollection<string> EventTypes { get; } = [StandingTexts.Event, StandingTexts.AnsweredEvent];

    public Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.EventType == StandingTexts.AnsweredEvent
            ? AnsweredAsync(message, cancellationToken)
            : StandingAsync(message, cancellationToken);
    }

    private async Task StandingAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { Template: { } template, DedupKey: { Length: > 0 } dedupKey } || !StandingTemplates.Contains(template)
            || payload.Sponsorship == Guid.Empty || !TryDate(payload.Date, out var date))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) does not name a template, a sponsorship and a day.");
        }

        // A sponsorship is never deleted, so a missing one is an error to retry and log.
        var to = await queries.GetStandingAsync(payload.Sponsorship, cancellationToken).ConfigureAwait(false)
                 ?? throw new InvalidOperationException($"Outbox message {message.Id} names sponsorship {payload.Sponsorship}, which does not exist.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["institution"] = InstitutionName(to.Language, to.InstitutionName, to.InstitutionNameSi, to.InstitutionNameTa),
            ["date"] = StandingTexts.Iso(date),
            ["time"] = to.StartTime,
        };

        if (TryDate(payload.Last, out var last))
        {
            values["last"] = StandingTexts.Iso(last);
        }

        if (TryDate(payload.Other, out var other))
        {
            values["other"] = StandingTexts.Iso(other);
        }

        if (to.DonorUserId is { } account)
        {
            await texts.SendAsync(account, template, values, dedupKey, cancellationToken).ConfigureAwait(false);
        }
        else if (to.TextConsent)
        {
            await texts.SendToPhoneAsync(to.Phone, to.Language, template, values, dedupKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AnsweredAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AnsweredPayload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { RequestNo: { Length: > 0 } requestNo })
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) has no request number.");
        }

        var request = await queries.GetDayRequestAsync(requestNo, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Outbox message {message.Id} names day request {requestNo}, which does not exist.");

        var template = payload.Given ? StandingTexts.RequestGiven : StandingTexts.RequestDeclined;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["requestNo"] = request.RequestNo,
            ["bookingNo"] = request.BookingNo ?? request.RequestNo,
            ["institution"] = InstitutionName(request.Language, request.InstitutionName, request.InstitutionNameSi, request.InstitutionNameTa),
            ["date"] = StandingTexts.Iso(request.Date),
            ["time"] = request.StartTime,
        };

        await texts.SendAsync(request.DonorUserId, template, values, $"{template}:{requestNo}", cancellationToken).ConfigureAwait(false);
    }

    private static string InstitutionName(string? language, string name, string? nameSi, string? nameTa) =>
        (language switch
        {
            "si" => nameSi,
            "ta" => nameTa,
            _ => null,
        }) is { Length: > 0 } local
            ? local
            : name;

    private static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private sealed record Payload(string? Template, Guid Sponsorship, string? Date, string? Last, string? Other, string? DedupKey);

    private sealed record AnsweredPayload(string? RequestNo, bool Given);
}
