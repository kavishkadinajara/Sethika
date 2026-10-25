namespace Sethika.Domain.Services;

/// <summary>
/// A leader's committed time as 15-minute blocks (Phase E, V2): the rows of
/// <c>booking.LeaderTimeBlocks</c> that an assignment or a leave period writes.
/// </summary>
/// <remarks>
/// The table's primary key <c>(LeaderId, BlockStart)</c> is what makes an overlap impossible, so this
/// only has to produce the right block starts: every 15 minutes from the start (less any buffer) up to,
/// not including, the end (plus the buffer). Two intervals that touch, one ending at 10:00 and the next
/// starting at 10:00, share no block. Times are UTC; Sri Lanka is UTC+05:30, so the grid is the same
/// in both (V7).
/// </remarks>
public static class TimeBlocks
{
    /// <summary>The grid, in minutes.</summary>
    public const int Minutes = 15;

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(Minutes);

    /// <summary>Whether <paramref name="time"/> sits on the 15-minute grid, to the tick.</summary>
    public static bool IsAligned(DateTime time) => time.Ticks % Step.Ticks == 0;

    /// <summary>Whether <paramref name="minutes"/> is a whole number of blocks (zero included).</summary>
    public static bool IsWhole(int minutes) => minutes >= 0 && minutes % Minutes == 0;

    /// <summary>
    /// The block starts that cover <c>[startsAt - buffer, endsAt + buffer)</c>.
    /// </summary>
    /// <param name="startsAt">UTC, on the grid.</param>
    /// <param name="endsAt">UTC, on the grid, after <paramref name="startsAt"/>.</param>
    /// <param name="bufferMinutes">Travel time either side (V5); a whole number of blocks.</param>
    public static IReadOnlyList<DateTime> For(DateTime startsAt, DateTime endsAt, int bufferMinutes = 0)
    {
        if (!IsAligned(startsAt) || !IsAligned(endsAt))
        {
            throw new ArgumentException("Times must be on the 15-minute grid.");
        }

        if (endsAt <= startsAt)
        {
            throw new ArgumentException("The end must come after the start.", nameof(endsAt));
        }

        if (!IsWhole(bufferMinutes))
        {
            throw new ArgumentOutOfRangeException(nameof(bufferMinutes), bufferMinutes, "A buffer is a whole number of 15-minute blocks.");
        }

        var buffer = TimeSpan.FromMinutes(bufferMinutes);
        var to = endsAt + buffer;
        var blocks = new List<DateTime>();
        for (var block = startsAt - buffer; block < to; block += Step)
        {
            blocks.Add(DateTime.SpecifyKind(block, DateTimeKind.Utc));
        }

        return blocks;
    }
}
