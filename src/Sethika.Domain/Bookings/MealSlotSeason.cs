namespace Sethika.Domain.Bookings;

/// <summary>
/// A window a slot is offered in, such as an iftar during Ramadan. Maps <c>booking.MealSlotSeasons</c>.
/// A slot with any seasons is bookable only inside one of them.
/// </summary>
public class MealSlotSeason
{
    public int Id { get; set; }
    public int MealSlotId { get; set; }
    public string Name { get; set; } = null!;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }

    public bool Covers(DateOnly date) => date >= DateFrom && date <= DateTo;
}
