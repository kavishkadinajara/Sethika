namespace Sethika.Domain.Bookings;

/// <summary>
/// The time of day a meal slot belongs to, read from its start time (plan 06, G2): morning before
/// 10:30, midday until 15:00, evening after.
/// </summary>
/// <remarks>
/// A slot has no meal type, and adding one would ask every institution to classify slots it already
/// has. Dana times are fixed by custom (the morning dana, the noon meal before midday, the evening),
/// so the start time already says which meal it is. The boundaries are a search filter's default,
/// kept here and nowhere else; the database knows nothing of them.
/// </remarks>
public static class MealTimes
{
    /// <summary>Every slot, whatever its time.</summary>
    public const string Any = "any";

    public const string Morning = "morning";
    public const string Midday = "midday";
    public const string Evening = "evening";

    /// <summary>The first start time that is midday.</summary>
    public static readonly TimeOnly MiddayFrom = new(10, 30);

    /// <summary>The first start time that is evening.</summary>
    public static readonly TimeOnly EveningFrom = new(15, 0);

    /// <summary>Every value a search may ask for.</summary>
    public static IReadOnlyList<string> All { get; } = [Any, Morning, Midday, Evening];

    /// <summary><see cref="Morning"/>, <see cref="Midday"/> or <see cref="Evening"/>: the meal a slot starting at <paramref name="start"/> is.</summary>
    public static string Of(TimeOnly start) =>
        start < MiddayFrom ? Morning
        : start < EveningFrom ? Midday
        : Evening;

    /// <summary>Whether a slot starting at <paramref name="start"/> answers a search for <paramref name="timeOfDay"/>.</summary>
    public static bool Matches(string timeOfDay, TimeOnly start) => timeOfDay == Any || timeOfDay == Of(start);
}
