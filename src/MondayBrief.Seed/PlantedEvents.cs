namespace MondayBrief.Seed;

/// <summary>
/// Every date the demo story depends on, in one place. The evals and the demo script reference these.
/// </summary>
public static class PlantedEvents
{
    public const ulong DefaultSeed = 20260831;

    // Data window: exactly 52 Monday-Sunday weeks. "Today" in the app is the Monday after.
    public static readonly DateOnly DataStart = new(2025, 9, 1);
    public static readonly DateOnly DataEnd = new(2026, 8, 30);
    public static readonly DateOnly AsOfDate = new(2026, 8, 31);

    public const string TimeZoneId = "America/Chicago";

    // 1. Mardi Gras spike: in-store lift from late January, +70-80% over the final 10 days, sharp drop Ash Wednesday.
    public static readonly DateOnly TwelfthNight = new(2026, 1, 6);
    public static readonly DateOnly MardiGrasRampStart = new(2026, 1 , 20);
    public static readonly DateOnly MardiGrasPeakStart = new(2026, 2, 8);
    public static readonly DateOnly FatTuesday = new(2026, 2, 17);
    public static readonly DateOnly AshWednesday = new(2026, 2, 18);
    public static readonly DateOnly MardiGradHangoverEnd = new(2026, 2, 22);

    // 2. Weather closure: tropical storm. Store closed two days, reduced hours either side.
    public static readonly DateOnly StormPrepDay = new(2026, 8, 11);
    public static readonly DateOnly StormClosedStart = new(2026, 8, 12);
    public static readonly DateOnly StormClosedEnd = new(2026, 8, 13);
    public static readonly DateOnly StormCleanupDay = new(2026, 8, 14);

    // 3. Holiday ramp. Thanksgiving and Christmas closures are normal, predictable closures.
    public static readonly DateOnly HolidayRampStart = new(2025, 11, 15);
    public static readonly DateOnly Thanksgiving = new(2025, 11, 27);
    public static readonly DateOnly BlackFriday = new(2025, 11, 28);
    public static readonly DateOnly SmallBusinessSaturday = new(2025, 11, 29);
    public static readonly DateOnly CyberMonday = new(2025, 12, 1);
    public static readonly DateOnly HolidayPeakStart = new(2025, 12, 13);
    public static readonly DateOnly OnlineShippingCutoff = new(2025, 12, 18);
    public static readonly DateOnly ChristmasEve = new(2025, 12, 24);
    public static readonly DateOnly Christmas = new(2025, 12, 25);

    // 4. Quiet decline: slow at first, then accelerating (1 - 0.80·t²). About half of June's rate by August.
    public const string DecliningSku = "LG-CNDL-01";
    public static readonly DateOnly DeclineStart = new(2026, 6, 1);
    public static readonly DateOnly DeclineEnd = new(2026, 8, 30);
    public const double DeclineDepth = 0.80;

    // 5. Online conversion drop after an (off-data) site change. Sessions stay flat.
    public static readonly DateOnly ConversionDropStart = new(2026, 7, 13);
    public const double ConversionBefore = 0.024;
    public const double ConversionAfter = 0.016;

    public static bool InRange(DateOnly d, DateOnly start, DateOnly endInclusive) => d >= start && d <= endInclusive;

    /// <summary>Position of <paramref name="d"/> between start and end, clamped to [0, 1].</summary>
    public static double Progress(DateOnly d, DateOnly start, DateOnly end)
    {
        var span = end.DayNumber - start.DayNumber;
        if (span <= 0)
        {
            return 1;
        }

        return Math.Clamp((d.DayNumber - start.DayNumber) / (double)span, 0, 1);
    }
}