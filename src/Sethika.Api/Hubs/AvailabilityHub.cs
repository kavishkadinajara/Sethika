using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sethika.Application.Abstractions;

namespace Sethika.Api.Hubs;

/// <summary>
/// Live slot updates (plan 07, H9): a calendar open on an institution's page watches it, and hears
/// <see cref="ChangedMessage"/> when a booking, a decision, a slot or a blocked day changes its days.
/// </summary>
/// <remarks>
/// Anonymous, like the availability it hints at. Under <c>/api</c>, so the web's one proxy rule (A3)
/// carries it and the browser still talks to one origin. A connection watches a few institutions at
/// most: each group is a Redis subscription on the backplane, so an unbounded number would let one
/// client grow every instance's subscriptions.
/// </remarks>
[AllowAnonymous]
public sealed class AvailabilityHub : Hub
{
    public const string Path = "/api/hubs/availability";

    /// <summary>The message a watching calendar receives: an <see cref="AvailabilityChangedMessage"/>.</summary>
    public const string ChangedMessage = "AvailabilityChanged";

    /// <summary>A page, a calendar and a spare: more than any screen needs.</summary>
    public const int MaxWatched = 3;

    /// <summary><c>org.Institutions.Slug</c>'s length; anything longer cannot be one.</summary>
    private const int MaxSlugLength = 150;

    private const string WatchedKey = "watched";

    /// <summary>The SignalR group of the calendars open on <paramref name="slug"/>.</summary>
    public static string GroupOf(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        return "institution:" + slug.Trim();
    }

    /// <summary>Starts sending this connection the changes of <paramref name="slug"/>'s days.</summary>
    public async Task Watch(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Trim().Length > MaxSlugLength)
        {
            throw new HubException("invalid_slug");
        }

        var watched = Watched();
        if (!watched.Contains(slug.Trim()) && watched.Count >= MaxWatched)
        {
            throw new HubException("too_many_watched");
        }

        watched.Add(slug.Trim());
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(slug), Context.ConnectionAborted).ConfigureAwait(false);
    }

    /// <summary>Stops them, as a calendar does when it moves to another institution.</summary>
    public async Task Unwatch(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        Watched().Remove(slug.Trim());
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(slug), Context.ConnectionAborted).ConfigureAwait(false);
    }

    private HashSet<string> Watched()
    {
        if (Context.Items.TryGetValue(WatchedKey, out var value) && value is HashSet<string> watched)
        {
            return watched;
        }

        var created = new HashSet<string>(StringComparer.Ordinal);
        Context.Items[WatchedKey] = created;
        return created;
    }
}

/// <summary>What a watching calendar hears: which institution, and which days (both <see langword="null"/>: every day).</summary>
/// <param name="From"><c>yyyy-MM-dd</c>.</param>
/// <param name="To"><c>yyyy-MM-dd</c>.</param>
public sealed record AvailabilityChangedMessage(string Slug, string? From, string? To);

/// <summary>Sends <see cref="AvailabilityHub.ChangedMessage"/> through the hub, and so through the Redis backplane to every instance.</summary>
internal sealed class HubAvailabilityNotifier(IHubContext<AvailabilityHub> hub) : IAvailabilityNotifier
{
    public Task ChangedAsync(string slug, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group(AvailabilityHub.GroupOf(slug))
            .SendAsync(AvailabilityHub.ChangedMessage, new AvailabilityChangedMessage(slug, Iso(firstDay), Iso(lastDay)), cancellationToken);

    private static string? Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
