namespace Sethika.Domain.Bookings;

/// <summary>One transition of a booking. Maps <c>booking.BookingStatusHistory</c>.</summary>
public class BookingStatusChange
{
    public long Id { get; set; }
    public long BookingId { get; set; }

    /// <summary><see langword="null"/> for the booking being made.</summary>
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Comment { get; set; }
}
